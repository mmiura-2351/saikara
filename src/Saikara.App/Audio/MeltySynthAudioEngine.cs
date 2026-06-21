using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.UI.Dispatching;
using NAudio.Wave;
using Saikara.Core.Audio;
using Saikara.Core.Lyrics;
using Saikara.Core.Midi;

// 'PlaybackState' exists in both Saikara.Core.Audio and NAudio.Wave; alias the Core one
// (the IAudioEngine contract type) so unqualified references resolve unambiguously.
using PlaybackState = Saikara.Core.Audio.PlaybackState;

namespace Saikara.App.Audio;

/// <summary>
/// <see cref="IAudioEngine"/> implementation that combines MeltySynth (pure-C# SoundFont
/// synthesis) with NAudio (WASAPI / WaveOut output). It renders the loaded <see cref="MidiSong"/>
/// through a <see cref="MeltySynth.MidiFileSequencer"/> driven SoundFont voice, and applies key/tempo by
/// re-deriving the playing sequence from the untransformed source song via
/// <see cref="MidiTransforms"/>, re-serializing it with <see cref="MidiSerializer"/>, and
/// re-loading it into the sequencer — exactly as the <see cref="IAudioEngine"/> contract
/// prescribes, so no real-time DSP lives here.
/// </summary>
/// <remarks>
/// <para>
/// Threading: NAudio pulls audio on its own playback thread via the inner
/// <see cref="SequencerSampleProvider"/>. Every access to the synthesizer / sequencer is
/// serialized through <see cref="_renderLock"/>; transport methods take the same lock so a
/// rebuild never races a render. <see cref="PositionChanged"/> is raised from a
/// <see cref="DispatcherQueueTimer"/> on the UI thread; <see cref="StateChanged"/> is raised
/// inline on whichever thread caused the transition (the contract permits a background thread).
/// </para>
/// <para>
/// Seeking is best-effort. MeltySynth's <see cref="MeltySynth.MidiFileSequencer"/> exposes a read-only
/// <see cref="MeltySynth.MidiFileSequencer.Position"/> and offers no seek primitive, so we approximate a
/// seek by replaying from the start and rendering-then-discarding audio up to the target
/// position (a silent fast-forward). This is exact to within one render block and is the same
/// mechanism used to preserve the position across a key/tempo rebuild.
/// </para>
/// <para>
/// If the SoundFont file is missing the engine constructs in a disabled state
/// (<see cref="IsPlaybackEnabled"/> == <see langword="false"/>): transport calls become no-ops
/// and <see cref="Load(MidiSong)"/> still records duration so the UI can show the song, but no
/// audio device is opened. This keeps the app usable when the first-run download failed.
/// </para>
/// </remarks>
public sealed class MeltySynthAudioEngine : IAudioEngine, ITelopSource
{
    /// <summary>Output sample rate. 44.1 kHz stereo is universally supported by shared-mode WASAPI.</summary>
    private const int SampleRate = 44100;

    private readonly object _renderLock = new();
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer? _positionTimer;
    private readonly MidiSerializer _serializer = new();
    private readonly LyricTelopBuilder _telopBuilder = new();

    /// <summary>
    /// Telop lines for the currently loaded sequence, built from the tempo-transformed song so
    /// syllable start-times match the playback clock. Rebuilt by <see cref="BuildAndLoadSequence"/>;
    /// never <see langword="null"/>.
    /// </summary>
    private IReadOnlyList<TelopLine> _telopLines = Array.Empty<TelopLine>();

    private readonly MeltySynth.Synthesizer? _synthesizer;
    private readonly MeltySynth.MidiFileSequencer? _sequencer;
    private readonly SequencerSampleProvider? _sampleProvider;
    private readonly IWavePlayer? _output;

    /// <summary>The untransformed source song last passed to <see cref="Load(MidiSong)"/>.</summary>
    private MidiSong? _sourceSong;

    private int _semitoneOffset;
    private double _tempoPercent = 100.0;

    private PlaybackState _state = PlaybackState.Stopped;
    private TimeSpan _duration = TimeSpan.Zero;

    private bool _disposed;

    /// <summary>
    /// Creates the engine. When <paramref name="soundFontPath"/> does not exist the engine is
    /// constructed disabled (see the type remarks) rather than throwing, so a failed first-run
    /// SoundFont download does not take down the app.
    /// </summary>
    /// <param name="soundFontPath">
    /// Absolute path to the <c>.sf2</c> SoundFont (typically
    /// <c>SoundFontInstaller.DefaultSoundFontPath</c>).
    /// </param>
    /// <param name="dispatcherQueue">
    /// The UI dispatcher used to raise <see cref="PositionChanged"/> on the UI thread. Pass
    /// <c>DispatcherQueue.GetForCurrentThread()</c> from the UI thread.
    /// </param>
    public MeltySynthAudioEngine(string soundFontPath, DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(soundFontPath);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);
        _dispatcherQueue = dispatcherQueue;

        if (!File.Exists(soundFontPath))
        {
            // Disabled mode: no synth, no device. Transport is inert; Load still records duration.
            return;
        }

        try
        {
            _synthesizer = new MeltySynth.Synthesizer(soundFontPath, SampleRate);
            _sequencer = new MeltySynth.MidiFileSequencer(_synthesizer);
            _sampleProvider = new SequencerSampleProvider(_sequencer, _renderLock, SampleRate);

            _output = CreateOutput();
            _output.Init(_sampleProvider);

            _positionTimer = _dispatcherQueue.CreateTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(50);
            _positionTimer.Tick += OnPositionTimerTick;
        }
        catch
        {
            // Any synth/device init failure leaves the engine disabled rather than crashing.
            DisposeOutput();
            _synthesizer = null;
            _sequencer = null;
            _sampleProvider = null;
            _output = null;
        }
    }

    /// <summary>
    /// <see langword="true"/> when a SoundFont loaded and an audio device was opened, so playback
    /// is possible. <see langword="false"/> means the engine is in disabled mode and transport
    /// calls are no-ops. Bind a status message off this in the UI.
    /// </summary>
    public bool IsPlaybackEnabled => _output is not null;

    /// <inheritdoc />
    public PlaybackState State => _state;

    /// <inheritdoc />
    public TimeSpan Duration => _duration;

    /// <inheritdoc />
    public TimeSpan Position
    {
        get
        {
            if (!IsPlaybackEnabled || _sequencer is null)
            {
                return TimeSpan.Zero;
            }

            lock (_renderLock)
            {
                // The sequence is always (re)played from the start and then fast-forwarded to the
                // desired offset, so the sequencer's own Position is the true song position.
                TimeSpan position = _sequencer.Position;
                return position > _duration ? _duration : position;
            }
        }
    }

    /// <inheritdoc />
    public int SemitoneOffset
    {
        get => _semitoneOffset;
        set
        {
            if (_semitoneOffset == value)
            {
                return;
            }

            _semitoneOffset = value;
            RebuildPreservingPosition();
        }
    }

    /// <inheritdoc />
    public double TempoPercent
    {
        get => _tempoPercent;
        set
        {
            if (value <= 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Tempo percent must be positive.");
            }

            if (_tempoPercent.Equals(value))
            {
                return;
            }

            _tempoPercent = value;
            RebuildPreservingPosition();
        }
    }

    /// <inheritdoc />
    public event EventHandler? StateChanged;

    /// <inheritdoc />
    public event EventHandler? PositionChanged;

    /// <inheritdoc cref="ITelopSource.CurrentTelopLines" />
    public IReadOnlyList<TelopLine> CurrentTelopLines => _telopLines;

    /// <inheritdoc cref="ITelopSource.TelopChanged" />
    public event EventHandler? TelopChanged;

    /// <inheritdoc />
    public void Load(MidiSong song)
    {
        ArgumentNullException.ThrowIfNull(song);
        ThrowIfDisposed();

        _sourceSong = song;

        // Stop any current playback and rewind before swapping the sequence.
        if (IsPlaybackEnabled)
        {
            StopOutput();
        }

        BuildAndLoadSequence(TimeSpan.Zero);
        SetState(PlaybackState.Stopped);
    }

    /// <inheritdoc />
    public void Play()
    {
        ThrowIfDisposed();
        if (_sourceSong is null)
        {
            throw new InvalidOperationException("No song has been loaded.");
        }

        if (!IsPlaybackEnabled || _output is null)
        {
            // Disabled mode: nothing to play. Stay Stopped.
            return;
        }

        if (_state == PlaybackState.Playing)
        {
            return;
        }

        lock (_renderLock)
        {
            _sampleProvider!.IsRendering = true;
        }

        _output.Play();
        _positionTimer?.Start();
        SetState(PlaybackState.Playing);
    }

    /// <inheritdoc />
    public void Pause()
    {
        ThrowIfDisposed();
        if (_state != PlaybackState.Playing || _output is null)
        {
            return;
        }

        // Keep Position: stop pulling audio but do NOT replay. The sequencer's Position is
        // frozen because the provider stops advancing it.
        lock (_renderLock)
        {
            _sampleProvider!.IsRendering = false;
        }

        _output.Pause();
        _positionTimer?.Stop();
        SetState(PlaybackState.Paused);
    }

    /// <inheritdoc />
    public void Stop()
    {
        ThrowIfDisposed();
        if (_state == PlaybackState.Stopped)
        {
            return;
        }

        StopOutput();
        BuildAndLoadSequence(TimeSpan.Zero);
        SetState(PlaybackState.Stopped);
    }

    /// <inheritdoc />
    public void Seek(TimeSpan position)
    {
        ThrowIfDisposed();
        if (_sourceSong is null || !IsPlaybackEnabled)
        {
            return;
        }

        TimeSpan target = Clamp(position, TimeSpan.Zero, _duration);
        bool wasPlaying = _state == PlaybackState.Playing;

        // Pause the pull side, and the device itself, so the synchronous fast-forward below is
        // neither consumed by nor starves the render thread (an underrun is heard as a momentary
        // glitch). The device is resumed after the swap completes.
        lock (_renderLock)
        {
            _sampleProvider!.IsRendering = false;
        }
        if (wasPlaying)
        {
            _output!.Pause();
        }

        BuildAndLoadSequence(target);

        if (wasPlaying)
        {
            _output!.Play();
            lock (_renderLock)
            {
                _sampleProvider!.IsRendering = true;
            }
        }

        RaisePositionChanged();
    }

    /// <summary>
    /// Re-derives the playing sequence from the source song with the current key/tempo and
    /// re-loads it into the sequencer, fast-forwarding to <paramref name="startAt"/> so playback
    /// resumes from there. Must be called under no lock; it takes <see cref="_renderLock"/> itself.
    /// </summary>
    private void BuildAndLoadSequence(TimeSpan startAt)
    {
        if (_sourceSong is null)
        {
            _duration = TimeSpan.Zero;
            return;
        }

        // Apply key then tempo on top of the untouched source, mirroring the contract.
        MidiSong transformed = MidiTransforms.Transpose(_sourceSong, _semitoneOffset);
        transformed = MidiTransforms.ScaleTempo(transformed, _tempoPercent);
        byte[] bytes = _serializer.ToBytes(transformed);

        // Duration tracks the transformed (tempo-scaled) song, as the contract requires.
        _duration = transformed.Duration;

        // Rebuild the telop from the transformed song so syllable times line up with the clock.
        // This runs even in disabled mode so the display still shows lyrics without audio.
        RebuildTelopFrom(transformed);

        if (!IsPlaybackEnabled || _sequencer is null)
        {
            return;
        }

        using var stream = new MemoryStream(bytes, writable: false);
        var midiFile = new MeltySynth.MidiFile(stream);

        lock (_renderLock)
        {
            // Reset the synthesizer before swapping the sequence (key/tempo change or seek):
            // any voice still sounding from the previous sequence has no matching NoteOff in
            // the new event stream, so without this it hangs as a continuous tone (a "beep").
            _synthesizer!.Reset();
            _sequencer!.Play(midiFile, loop: false);

            // Fast-forward: render and discard audio up to the seek/rebuild target. Position is
            // read-only and cannot be set, so a silent render advances the sequencer instead.
            FastForwardLocked(startAt);
        }
    }

    /// <summary>
    /// Advances the sequencer to <paramref name="target"/> by rendering into a scratch buffer and
    /// discarding it. Caller must hold <see cref="_renderLock"/>. Accurate to one render block.
    /// </summary>
    private void FastForwardLocked(TimeSpan target)
    {
        if (target <= TimeSpan.Zero || _sequencer is null)
        {
            return;
        }

        // One block of stereo scratch. We loop until the sequencer's relative Position catches up
        // or the sequence ends, so we never spin forever on a short song.
        const int block = 4096;
        var left = new float[block];
        var right = new float[block];

        while (_sequencer.Position < target && !_sequencer.EndOfSequence)
        {
            _sequencer.Render(left, right);
        }
    }

    /// <summary>
    /// Re-derives the sequence in place after a key or tempo change, preserving the current song
    /// position so the change "applies while playing without losing the current position".
    /// </summary>
    private void RebuildPreservingPosition()
    {
        if (_disposed || _sourceSong is null)
        {
            return;
        }

        TimeSpan current = Position;

        bool wasPlaying = _state == PlaybackState.Playing;
        if (IsPlaybackEnabled)
        {
            lock (_renderLock)
            {
                _sampleProvider!.IsRendering = false;
            }

            // Pause the device across the synchronous rebuild/fast-forward below. While the UI
            // thread holds the render lock to swap the sequence, a still-running device starves
            // and underruns, which is heard as a momentary glitch. Resumed after the swap.
            if (wasPlaying)
            {
                _output!.Pause();
            }
        }

        // Re-derive at the same wall-clock position. For a key change the timeline is unchanged
        // so this is exact; for a tempo change it preserves elapsed wall-clock time (the musical
        // point shifts slightly), and FastForwardLocked clamps at end-of-sequence for a now-shorter
        // song. Good enough for P1; documented as best-effort.
        BuildAndLoadSequence(current);

        if (wasPlaying && IsPlaybackEnabled)
        {
            _output!.Play();
            lock (_renderLock)
            {
                _sampleProvider!.IsRendering = true;
            }
        }

        RaisePositionChanged();
    }

    private void StopOutput()
    {
        if (_output is null)
        {
            return;
        }

        lock (_renderLock)
        {
            _sampleProvider!.IsRendering = false;
        }

        _output.Stop();
        _positionTimer?.Stop();
    }

    private void OnPositionTimerTick(DispatcherQueueTimer sender, object args)
    {
        // If the sequence reached its end, settle into Stopped and rewind.
        if (_state == PlaybackState.Playing && _sequencer is not null)
        {
            bool ended;
            lock (_renderLock)
            {
                ended = _sequencer.EndOfSequence;
            }

            if (ended)
            {
                StopOutput();
                BuildAndLoadSequence(TimeSpan.Zero);
                SetState(PlaybackState.Stopped);
                return;
            }
        }

        RaisePositionChanged();
    }

    private void RaisePositionChanged() => PositionChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Rebuilds <see cref="_telopLines"/> from the transformed song and raises
    /// <see cref="TelopChanged"/> (on the UI thread) when the line set actually changed. Seeks and
    /// key changes leave the lyric timeline untouched, so the rebuilt lines compare equal and no
    /// event fires; a load or a tempo change produces a different set and notifies the view.
    /// </summary>
    private void RebuildTelopFrom(MidiSong transformed)
    {
        IReadOnlyList<TelopLine> rebuilt = _telopBuilder.Build(transformed);
        if (TelopLinesEqual(_telopLines, rebuilt))
        {
            return;
        }

        _telopLines = rebuilt;
        RaiseTelopChanged();
    }

    /// <summary>
    /// Raises <see cref="TelopChanged"/> on the UI thread. <see cref="BuildAndLoadSequence"/> can run
    /// from a background thread (a key/tempo setter touched off the UI thread), so marshal to keep
    /// the contract that the event arrives on the UI thread.
    /// </summary>
    private void RaiseTelopChanged()
    {
        if (TelopChanged is null)
        {
            return;
        }

        if (_dispatcherQueue.HasThreadAccess)
        {
            TelopChanged.Invoke(this, EventArgs.Empty);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => TelopChanged?.Invoke(this, EventArgs.Empty));
        }
    }

    /// <summary>
    /// Cheap structural comparison of two telop line sets: equal when they have the same number of
    /// lines and each pair agrees on start time and text. Sufficient to suppress redundant
    /// <see cref="TelopChanged"/> events on seeks/key changes (which don't touch the lyric timeline).
    /// </summary>
    private static bool TelopLinesEqual(IReadOnlyList<TelopLine> a, IReadOnlyList<TelopLine> b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (a[i].StartTime != b[i].StartTime || !string.Equals(a[i].Text, b[i].Text, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private void SetState(PlaybackState newState)
    {
        if (_state == newState)
        {
            return;
        }

        _state = newState;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IWavePlayer CreateOutput()
    {
        // Prefer shared-mode WASAPI for low latency; fall back to WaveOutEvent if WASAPI cannot
        // be initialized (e.g. no default render endpoint). Both consume an ISampleProvider.
        try
        {
            return new NAudio.Wave.WasapiOut(
                NAudio.CoreAudioApi.AudioClientShareMode.Shared,
                useEventSync: true,
                latency: 100);
        }
        catch
        {
            return new WaveOutEvent();
        }
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void DisposeOutput()
    {
        try
        {
            _output?.Stop();
        }
        catch
        {
            // Ignore device teardown errors.
        }

        _output?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_positionTimer is not null)
        {
            _positionTimer.Stop();
            _positionTimer.Tick -= OnPositionTimerTick;
        }

        DisposeOutput();
    }

    /// <summary>
    /// NAudio <see cref="ISampleProvider"/> that pulls interleaved stereo float audio from the
    /// MeltySynth <see cref="MeltySynth.MidiFileSequencer"/>. When <see cref="IsRendering"/> is
    /// <see langword="false"/> (paused / stopped) it emits silence so the device stays open for a
    /// seamless resume, without advancing the sequencer's position.
    /// </summary>
    private sealed class SequencerSampleProvider : ISampleProvider
    {
        private readonly MeltySynth.MidiFileSequencer _sequencer;
        private readonly object _renderLock;

        // Per-channel scratch buffers; MeltySynth renders into separate L/R spans, which we then
        // interleave into NAudio's buffer.
        private float[] _left = new float[1024];
        private float[] _right = new float[1024];

        public SequencerSampleProvider(MeltySynth.MidiFileSequencer sequencer, object renderLock, int sampleRate)
        {
            _sequencer = sequencer;
            _renderLock = renderLock;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels: 2);
        }

        /// <summary>Stereo 32-bit IEEE float at the engine sample rate.</summary>
        public WaveFormat WaveFormat { get; }

        /// <summary>
        /// When <see langword="true"/> the provider renders from the sequencer; when
        /// <see langword="false"/> it fills with silence and leaves the sequencer untouched.
        /// </summary>
        public bool IsRendering { get; set; }

        /// <summary>
        /// Fills <paramref name="buffer"/> with <paramref name="count"/> interleaved stereo float
        /// samples. Always returns <paramref name="count"/> (emitting silence when not rendering
        /// or past the end) so NAudio keeps the device running and resume/seek work without a
        /// device restart.
        /// </summary>
        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2; // interleaved stereo: two samples per frame
            EnsureScratch(frames);

            if (!IsRendering)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            lock (_renderLock)
            {
                if (_sequencer.EndOfSequence)
                {
                    Array.Clear(buffer, offset, count);
                    return count;
                }

                Span<float> left = _left.AsSpan(0, frames);
                Span<float> right = _right.AsSpan(0, frames);
                _sequencer.Render(left, right);

                int o = offset;
                for (int i = 0; i < frames; i++)
                {
                    buffer[o++] = left[i];
                    buffer[o++] = right[i];
                }
            }

            return count;
        }

        private void EnsureScratch(int frames)
        {
            if (_left.Length < frames)
            {
                _left = new float[frames];
                _right = new float[frames];
            }
        }
    }
}
