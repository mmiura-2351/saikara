using System;
using Microsoft.UI.Dispatching;
using Saikara.Core.Audio;
using Saikara.Core.Pitch;

namespace Saikara.App.Audio;

/// <summary>
/// Lazily holds the singleton <see cref="IPitchMonitor"/>. Like <see cref="AudioEngineHolder"/>, the
/// monitor cannot be built when the DI host is constructed: it needs the UI thread's
/// <see cref="DispatcherQueue"/> (to marshal <see cref="IPitchMonitor.PitchDetected"/>) and the
/// constructed <see cref="IAudioEngine"/> (for the playback clock used to time-stamp samples), both
/// of which only exist once the UI is up. <see cref="App"/> calls <see cref="Initialize"/> from
/// <c>OnLaunched</c> on the UI thread after the audio engine is initialised.
/// </summary>
public sealed class PitchMonitorHolder : IDisposable
{
    private readonly IPitchDetector _detector;
    private PitchMonitor? _monitor;

    /// <summary>Creates the holder. The detector is DI-resolved and is safe to construct eagerly.</summary>
    public PitchMonitorHolder(IPitchDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    /// <summary>
    /// Builds the monitor against <paramref name="audioEngine"/> and <paramref name="dispatcherQueue"/>.
    /// Idempotent: a second call is ignored so re-entrant launches don't leak monitors.
    /// </summary>
    public void Initialize(IAudioEngine audioEngine, DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(audioEngine);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _monitor ??= new PitchMonitor(audioEngine, _detector, dispatcherQueue);
    }

    /// <summary>
    /// The constructed monitor.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if accessed before <see cref="Initialize"/>.</exception>
    public IPitchMonitor Monitor =>
        _monitor ?? throw new InvalidOperationException(
            "The pitch monitor has not been initialized yet. Call Initialize from OnLaunched first.");

    /// <inheritdoc />
    public void Dispose() => _monitor?.Dispose();
}
