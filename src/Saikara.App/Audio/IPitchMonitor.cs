using System;
using System.Collections.Generic;
using Saikara.Core.Scoring;

namespace Saikara.App.Audio;

/// <summary>
/// Captures the default microphone, runs each analysis hop through the Core pitch detector, and
/// exposes the result two ways: a live <see cref="LivePitch"/> for the real-time pitch bar, and a
/// growing, latency-corrected list of <see cref="PitchSample"/>s for end-of-song scoring (P4).
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle: the monitor is started when playback starts and stopped on pause/stop. While running
/// it captures from the default input device, slices the stream into overlapping frames
/// (frame ≈ 4096 samples, hop ≈ 1024 samples), detects pitch off the UI thread, and for each hop
/// (a) raises <see cref="PitchDetected"/> with the live result and (b) appends a
/// <see cref="PitchSample"/> whose <see cref="PitchSample.Time"/> is the engine playback position
/// at capture minus <see cref="LatencyOffset"/>.
/// </para>
/// <para>
/// Robustness: if no input device is available or capture fails to initialise, <see cref="Start"/>
/// is a logged no-op and <see cref="IsCapturing"/> stays <see langword="false"/> — the rest of the
/// app keeps working without a microphone.
/// </para>
/// <para>Threading: <see cref="PitchDetected"/> is raised on the UI thread. Implements <see cref="IDisposable"/>.</para>
/// </remarks>
public interface IPitchMonitor : IDisposable
{
    /// <summary>
    /// Capture-to-playback latency subtracted from the engine position when stamping each
    /// <see cref="PitchSample.Time"/>, so the sample lines up with the backing track it was sung
    /// against. This is the P3 latency-calibration knob; default ≈ 100 ms. Settable at any time.
    /// </summary>
    TimeSpan LatencyOffset { get; set; }

    /// <summary><see langword="true"/> while a capture device is open and feeding the detector.</summary>
    bool IsCapturing { get; }

    /// <summary>
    /// <see langword="false"/> when no input device could be opened (the monitor is inert).
    /// <see cref="Start"/> becomes a no-op. Surface this in the UI as "microphone unavailable".
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>The most recent live hop (or <see cref="LivePitch.Silent"/> before the first hop).</summary>
    LivePitch Latest { get; }

    /// <summary>
    /// The latency-corrected samples accumulated since the last <see cref="Reset"/>, in capture order
    /// (non-decreasing <see cref="PitchSample.Time"/>). Consumed by the P4 scoring engine at song end.
    /// A snapshot copy — safe to read while capturing.
    /// </summary>
    IReadOnlyList<PitchSample> CollectedSamples { get; }

    /// <summary>
    /// Raised on the UI thread once per analysis hop with the latest live result, for the pitch bar.
    /// </summary>
    event EventHandler<LivePitch>? PitchDetected;

    /// <summary>
    /// Opens the default input device and begins capture/detection. No-op if already capturing or
    /// <see cref="IsAvailable"/> is <see langword="false"/>. Never throws on a missing/locked device.
    /// </summary>
    void Start();

    /// <summary>Stops capture and releases the device. Accumulated samples are kept (use <see cref="Reset"/> to clear).</summary>
    void Stop();

    /// <summary>
    /// Clears <see cref="CollectedSamples"/> and the live result. Call at the start of each scored
    /// performance (e.g. when a song is (re)loaded) so the next run starts from an empty accumulator.
    /// </summary>
    void Reset();
}
