using System;
using Microsoft.UI.Dispatching;
using Saikara.Core.Audio;

namespace Saikara.App.Audio;

/// <summary>
/// Drives an <see cref="IPitchMonitor"/> from an <see cref="IAudioEngine"/>'s transport: starts
/// capture when playback starts and stops it on pause/stop. Also resets the sample accumulator when
/// playback (re)starts from the very beginning, so each performance is scored from a clean slate.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IAudioEngine.StateChanged"/> may, per its contract, arrive on a background thread, and
/// opening/closing a WASAPI capture device is best done on a consistent thread, so every transition
/// is marshaled onto the UI <see cref="DispatcherQueue"/> before touching the monitor.
/// </para>
/// <para>
/// The link is created once at startup (after both the engine and monitor exist) and lives for the
/// app's lifetime; <see cref="Dispose"/> detaches the handler.
/// </para>
/// </remarks>
public sealed class PitchMonitorTransportLink : IDisposable
{
    private readonly IAudioEngine _audioEngine;
    private readonly IPitchMonitor _monitor;
    private readonly DispatcherQueue _dispatcherQueue;

    private PlaybackState _lastState = PlaybackState.Stopped;
    private bool _disposed;

    public PitchMonitorTransportLink(IAudioEngine audioEngine, IPitchMonitor monitor, DispatcherQueue dispatcherQueue)
    {
        _audioEngine = audioEngine ?? throw new ArgumentNullException(nameof(audioEngine));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _dispatcherQueue = dispatcherQueue ?? throw new ArgumentNullException(nameof(dispatcherQueue));

        _audioEngine.StateChanged += OnEngineStateChanged;
    }

    private void OnEngineStateChanged(object? sender, EventArgs e)
    {
        PlaybackState state = _audioEngine.State;

        if (_dispatcherQueue.HasThreadAccess)
        {
            Apply(state);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() => Apply(state));
        }
    }

    private void Apply(PlaybackState state)
    {
        if (_disposed)
        {
            return;
        }

        switch (state)
        {
            case PlaybackState.Playing:
                // Starting from a Stopped state means a fresh run from the top — clear old samples so
                // scoring measures only this performance. Resuming from Paused keeps the accumulator.
                if (_lastState == PlaybackState.Stopped)
                {
                    _monitor.Reset();
                }

                _monitor.Start();
                break;

            case PlaybackState.Paused:
            case PlaybackState.Stopped:
                _monitor.Stop();
                break;
        }

        _lastState = state;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _audioEngine.StateChanged -= OnEngineStateChanged;
    }
}
