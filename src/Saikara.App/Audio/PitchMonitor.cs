using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.UI.Dispatching;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Saikara.Core.Audio;
using Saikara.Core.Pitch;
using Saikara.Core.Scoring;

namespace Saikara.App.Audio;

/// <summary>
/// Default <see cref="IPitchMonitor"/>: WASAPI (with WaveIn fallback) microphone capture feeding a
/// per-hop <see cref="McLeodPitchDetector"/>, producing both a live result for the pitch bar and a
/// latency-corrected <see cref="PitchSample"/> stream for scoring.
/// </summary>
/// <remarks>
/// <para>
/// <b>Capture.</b> The default input device is opened with <see cref="WasapiCapture"/>; if that
/// throws (no endpoint, exclusive lock, unsupported format) capture falls back to
/// <see cref="WaveInEvent"/>. Both are <see cref="IWaveIn"/> and raise <see cref="IWaveIn.DataAvailable"/>
/// on a capture thread (NOT the UI thread). The device's <see cref="WaveFormat"/> may be 32-bit IEEE
/// float (typical for shared-mode WASAPI) or 16-bit PCM (WaveInEvent); both are converted to mono
/// float in <c>[-1, 1]</c> by <see cref="ConvertToMono"/>.
/// </para>
/// <para>
/// <b>Pipeline / threading.</b> The capture callback only converts to mono float and enqueues it
/// (cheap, never blocks the device thread). A dedicated detection thread drains the queue into a
/// sliding window of <see cref="FrameSize"/> samples advanced by <see cref="HopSize"/>, runs the
/// detector per hop, computes hop RMS, then (a) marshals <see cref="PitchDetected"/> to the UI thread
/// and (b) appends a latency-corrected <see cref="PitchSample"/>. Detection therefore runs off both
/// the UI thread and the capture thread. <see cref="McLeodPitchDetector"/> is not thread-safe, but
/// only this single detection thread ever touches it.
/// </para>
/// <para>
/// <b>Time base.</b> Each hop is stamped with the engine's current <see cref="IAudioEngine.Position"/>
/// (read at detection time) minus <see cref="LatencyOffset"/>. This is approximate — the position is
/// sampled when the hop is detected, not when its audio entered the mic — but the fixed
/// <see cref="LatencyOffset"/> absorbs the bulk of the capture+buffer delay and is the P3 calibration
/// knob. Negative results are clamped to <see cref="TimeSpan.Zero"/>.
/// </para>
/// </remarks>
public sealed class PitchMonitor : IPitchMonitor
{
    /// <summary>Analysis window length in samples. 4096 @ 44.1 kHz ≈ 93 ms — enough low-pitch resolution.</summary>
    private const int FrameSize = 4096;

    /// <summary>Hop (advance) between successive analysis frames. 1024 @ 44.1 kHz ≈ 43 hops/s.</summary>
    private const int HopSize = 1024;

    /// <summary>Detector sample rate. The captured stream is resampled by selection, not conversion: see remarks.</summary>
    private const int DetectionSampleRate = 44100;

    private readonly IAudioEngine _audioEngine;
    private readonly IPitchDetector _detector;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly object _gate = new();

    /// <summary>Producer/consumer buffer of converted mono float samples awaiting detection.</summary>
    private readonly Queue<float> _pending = new();

    /// <summary>Signals the detection thread that new samples (or a stop request) are available.</summary>
    private readonly AutoResetEvent _samplesReady = new(false);

    /// <summary>Sliding analysis window; owned solely by the detection thread.</summary>
    private readonly float[] _window = new float[FrameSize];

    /// <summary>Accumulated latency-corrected samples for scoring. Guarded by <see cref="_gate"/>.</summary>
    private readonly List<PitchSample> _collected = new();

    private IWaveIn? _capture;
    private int _captureSampleRate = DetectionSampleRate;
    private int _captureChannels = 1;
    private bool _captureIsFloat = true;
    private int _captureBitsPerSample = 32;

    private Thread? _detectionThread;
    private volatile bool _running;
    private LivePitch _latest = LivePitch.Silent;
    private bool _isAvailable = true;
    private bool _disposed;

    /// <summary>
    /// Creates the monitor. The engine supplies the playback clock used to time-stamp samples; the
    /// detector analyses each hop; the dispatcher marshals <see cref="PitchDetected"/> to the UI.
    /// </summary>
    public PitchMonitor(IAudioEngine audioEngine, IPitchDetector detector, DispatcherQueue dispatcherQueue)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));
    }

    /// <inheritdoc />
    public TimeSpan LatencyOffset { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <inheritdoc />
    public bool IsCapturing => _running;

    /// <inheritdoc />
    public bool IsAvailable => _isAvailable;

    /// <inheritdoc />
    public LivePitch Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<PitchSample> CollectedSamples
    {
        get
        {
            lock (_gate)
            {
                // Snapshot so callers can iterate while capture keeps appending.
                return _collected.ToArray();
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<LivePitch>? PitchDetected;

    /// <inheritdoc />
    public void Start()
    {
        if (_disposed || _running || !_isAvailable)
        {
            return;
        }

        try
        {
            _capture = CreateCapture();
            CacheFormat(_capture.WaveFormat);
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
        }
        catch (Exception ex)
        {
            // No device / permission denied / format unsupported: disable and stay silent.
            Debug.WriteLine($"[PitchMonitor] capture init failed, disabling: {ex}");
            DisposeCapture();
            _isAvailable = false;
            return;
        }

        lock (_gate)
        {
            _pending.Clear();
            Array.Clear(_window);
            _windowFill = 0;
        }

        _running = true;

        _detectionThread = new Thread(DetectionLoop)
        {
            Name = "Saikara.PitchMonitor",
            IsBackground = true,
        };
        _detectionThread.Start();

        try
        {
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PitchMonitor] StartRecording failed, disabling: {ex}");
            _running = false;
            _samplesReady.Set();
            DisposeCapture();
            _isAvailable = false;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _running = false;

        try
        {
            _capture?.StopRecording();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PitchMonitor] StopRecording threw (ignored): {ex}");
        }

        // Wake the detection thread so it observes _running == false and exits, then join.
        _samplesReady.Set();
        _detectionThread?.Join(TimeSpan.FromSeconds(1));
        _detectionThread = null;

        DisposeCapture();
    }

    /// <inheritdoc />
    public void Reset()
    {
        lock (_gate)
        {
            _collected.Clear();
            _pending.Clear();
            Array.Clear(_window);
            _windowFill = 0;
            _latest = LivePitch.Silent;
        }

        RaisePitchDetected(LivePitch.Silent);
    }

    // ---- Capture callbacks (capture thread) ----

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (!_running)
        {
            return;
        }

        // Convert the device's frame to mono float and enqueue. Kept minimal so the capture thread
        // is never blocked by detection.
        int produced;
        lock (_gate)
        {
            produced = ConvertToMono(e.Buffer, e.BytesRecorded, _pending);
        }

        if (produced > 0)
        {
            _samplesReady.Set();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Debug.WriteLine($"[PitchMonitor] recording stopped with error: {e.Exception}");
        }
    }

    // ---- Detection loop (dedicated background thread) ----

    /// <summary>Current number of valid samples held in <see cref="_window"/> (≤ <see cref="FrameSize"/>).</summary>
    private int _windowFill;

    private void DetectionLoop()
    {
        var hop = new float[HopSize];

        while (_running)
        {
            _samplesReady.WaitOne();

            while (_running)
            {
                // Pull one hop's worth of samples out of the pending queue under the lock.
                int got;
                lock (_gate)
                {
                    if (_pending.Count < HopSize)
                    {
                        break; // wait for more
                    }

                    for (int i = 0; i < HopSize; i++)
                    {
                        hop[i] = _pending.Dequeue();
                    }

                    got = HopSize;
                }

                AdvanceWindow(hop, got);

                // Only analyse once the window is full (first FrameSize/HopSize hops fill it).
                if (_windowFill < FrameSize)
                {
                    continue;
                }

                ProcessFrame();
            }
        }
    }

    /// <summary>
    /// Slides <paramref name="hop"/> into <see cref="_window"/>: discard the oldest <paramref name="count"/>
    /// samples and append the new ones at the end. Window is owned by the detection thread.
    /// </summary>
    private void AdvanceWindow(float[] hop, int count)
    {
        if (_windowFill < FrameSize)
        {
            // Filling phase: append until full.
            int room = FrameSize - _windowFill;
            int take = Math.Min(room, count);
            Array.Copy(hop, 0, _window, _windowFill, take);
            _windowFill += take;
            return;
        }

        // Full: shift left by count, then append the hop at the tail.
        Array.Copy(_window, count, _window, 0, FrameSize - count);
        Array.Copy(hop, 0, _window, FrameSize - count, count);
    }

    private void ProcessFrame()
    {
        PitchResult result = _detector.Detect(_window.AsSpan(0, FrameSize), _captureSampleRate);
        double energy = ComputeRms(_window, FrameSize);

        var live = new LivePitch { Result = result, Energy = energy };

        // Latency-corrected playback time for scoring: sample the engine clock now and shift back.
        TimeSpan position = _audioEngine.Position;
        TimeSpan stamp = position - LatencyOffset;
        if (stamp < TimeSpan.Zero)
        {
            stamp = TimeSpan.Zero;
        }

        PitchSample sample = result.IsVoiced
            ? PitchSample.VoicedAt(stamp, result.Frequency, result.Clarity, energy)
            : PitchSample.UnvoicedAt(stamp, energy);

        lock (_gate)
        {
            _latest = live;
            _collected.Add(sample);
        }

        RaisePitchDetected(live);
    }

    // ---- Helpers ----

    private void RaisePitchDetected(LivePitch live)
    {
        if (PitchDetected is null)
        {
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            PitchDetected.Invoke(this, live);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => PitchDetected?.Invoke(this, live));
        }
    }

    /// <summary>
    /// Opens the default input device. Prefers <see cref="WasapiCapture"/> (low latency, default
    /// shared-mode endpoint); on failure falls back to <see cref="WaveInEvent"/>. The caller wraps
    /// this in try/catch and disables the monitor if both throw.
    /// </summary>
    private static IWaveIn CreateCapture()
    {
        try
        {
            // Default-endpoint ctor; shared-mode, event-driven. Throws if no capture endpoint exists.
            return new WasapiCapture();
        }
        catch
        {
            // WaveInEvent uses the legacy WaveIn API and a fixed PCM format; broadest compatibility.
            return new WaveInEvent();
        }
    }

    private void CacheFormat(WaveFormat format)
    {
        _captureSampleRate = format.SampleRate > 0 ? format.SampleRate : DetectionSampleRate;
        _captureChannels = Math.Max(1, format.Channels);
        _captureBitsPerSample = format.BitsPerSample;
        _captureIsFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
    }

    /// <summary>
    /// Converts a raw device buffer (16-bit PCM or 32-bit IEEE float, interleaved
    /// <see cref="_captureChannels"/>) into mono float in <c>[-1, 1]</c> by averaging channels, and
    /// enqueues the result into <paramref name="sink"/>. Returns the number of mono samples produced.
    /// Caller holds <see cref="_gate"/>.
    /// </summary>
    private int ConvertToMono(byte[] buffer, int bytesRecorded, Queue<float> sink)
    {
        int channels = _captureChannels;
        int produced = 0;

        if (_captureIsFloat && _captureBitsPerSample == 32)
        {
            int totalSamples = bytesRecorded / 4;
            for (int i = 0; i + channels <= totalSamples; i += channels)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                {
                    sum += BitConverter.ToSingle(buffer, (i + c) * 4);
                }

                sink.Enqueue(sum / channels);
                produced++;
            }
        }
        else if (_captureBitsPerSample == 16)
        {
            int totalSamples = bytesRecorded / 2;
            for (int i = 0; i + channels <= totalSamples; i += channels)
            {
                int sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += BitConverter.ToInt16(buffer, (i + c) * 2);
                }

                sink.Enqueue(sum / (channels * 32768f));
                produced++;
            }
        }
        else
        {
            // Unsupported bit depth (e.g. 24-bit packed). Treat as silence rather than misread it;
            // the device is still draining so we keep position advancing without bogus pitches.
            int approxFrames = _captureBitsPerSample > 0
                ? bytesRecorded / (channels * (_captureBitsPerSample / 8))
                : 0;
            for (int i = 0; i < approxFrames; i++)
            {
                sink.Enqueue(0f);
                produced++;
            }
        }

        return produced;
    }

    /// <summary>Clamped RMS energy of the first <paramref name="count"/> samples, in <c>[0, 1]</c>.</summary>
    private static double ComputeRms(float[] samples, int count)
    {
        if (count <= 0)
        {
            return 0.0;
        }

        double sumSquares = 0.0;
        for (int i = 0; i < count; i++)
        {
            double s = samples[i];
            sumSquares += s * s;
        }

        double rms = Math.Sqrt(sumSquares / count);
        return rms > 1.0 ? 1.0 : rms;
    }

    private void DisposeCapture()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;

        try
        {
            _capture.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PitchMonitor] capture dispose threw (ignored): {ex}");
        }

        _capture = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Stop();
        _samplesReady.Dispose();
    }
}
