using System;
using Microsoft.UI.Dispatching;
using Saikara.Core.Audio;

namespace Saikara.App.Audio;

/// <summary>
/// Lazily holds the singleton <see cref="IAudioEngine"/>. The engine cannot be built when the DI
/// host is constructed because it needs two things only available once the UI is up: the UI
/// thread's <see cref="DispatcherQueue"/> (for marshaling <see cref="IAudioEngine.PositionChanged"/>)
/// and the resolved SoundFont path (created/downloaded at startup). <see cref="App"/> calls
/// <see cref="Initialize"/> from <c>OnLaunched</c> on the UI thread before any view-model resolves
/// <see cref="IAudioEngine"/>; <see cref="Engine"/> then returns the constructed instance.
/// </summary>
public sealed class AudioEngineHolder : IDisposable
{
    private MeltySynthAudioEngine? _engine;

    /// <summary>
    /// Builds the engine for <paramref name="soundFontPath"/> using <paramref name="dispatcherQueue"/>.
    /// Idempotent: a second call is ignored so re-entrant launches don't leak engines.
    /// </summary>
    /// <param name="soundFontPath">Absolute path to the default SoundFont (may not exist — the engine handles that).</param>
    /// <param name="dispatcherQueue">The UI thread dispatcher, from <c>DispatcherQueue.GetForCurrentThread()</c>.</param>
    public void Initialize(string soundFontPath, DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(soundFontPath);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _engine ??= new MeltySynthAudioEngine(soundFontPath, dispatcherQueue);
    }

    /// <summary>
    /// The constructed engine.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if accessed before <see cref="Initialize"/>.</exception>
    public IAudioEngine Engine =>
        _engine ?? throw new InvalidOperationException(
            "The audio engine has not been initialized yet. Call Initialize from OnLaunched first.");

    /// <inheritdoc />
    public void Dispose() => _engine?.Dispose();
}
