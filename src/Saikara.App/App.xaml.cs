using System;
using System.IO;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Saikara.App.Audio;
using Saikara.App.Services;
using Saikara.App.ViewModels;
using Saikara.App.Views;
using Saikara.Core.Audio;
using Saikara.Core.Editor;
using Saikara.Core.History;
using Saikara.Core.Import;
using Saikara.Core.Library;
using Saikara.Core.Midi;
using Saikara.Core.Pitch;
using Saikara.Core.Scoring;

namespace Saikara.App;

/// <summary>
/// Application entry point. Builds the dependency-injection host (view-models and
/// services) and, on launch, opens the two-window layout described in REQUIREMENTS §5:
/// an operator window (primary monitor) and a display window (secondary monitor).
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The composition root. Exposed so windows/pages can resolve their view-models.
    /// </summary>
    public static IHost Host { get; private set; } = null!;

    private OperatorWindow? _operatorWindow;
    private DisplayWindow? _displayWindow;

    /// <summary>
    /// Wires mic capture to the playback transport for the app's lifetime. Held so it isn't GC'd and
    /// so it can be disposed; created in <see cref="InitializeAudioEngineAsync"/> once both the engine
    /// and the pitch monitor exist.
    /// </summary>
    private PitchMonitorTransportLink? _pitchTransportLink;

    public App()
    {
        InitializeComponent();
        Host = BuildHost();
    }

    /// <summary>
    /// Resolves a service from the DI container. Convenience for XAML code-behind.
    /// </summary>
    public static T GetService<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Create the SQLite schema before any window queries the library. The library
        // is a singleton, so this initialises the one shared instance. InitializeAsync
        // is idempotent (CREATE TABLE IF NOT EXISTS), so a repeat call is harmless.
        var library = GetService<ISongLibrary>();
        await library.InitializeAsync();

        // P5: create the score-history schema before any score is saved. Shares the same SQLite DB
        // file as the library (a separate Scores table); InitializeAsync is idempotent.
        var scoreHistory = GetService<IScoreHistory>();
        await scoreHistory.InitializeAsync();

        // P6: create the song-corrections schema before any correction is saved. Shares the same
        // SQLite DB file as the library (a separate SongCorrections table); InitializeAsync is
        // idempotent.
        var correctionsStore = GetService<ISongCorrectionsStore>();
        await correctionsStore.InitializeAsync();

        // P1 audio bootstrap. Ensure the default SoundFont exists (first-run download), then
        // build the audio engine on the UI thread so its DispatcherQueueTimer marshals
        // PositionChanged to the UI. Robust to download failure: the engine constructs in a
        // disabled state and the app still opens (playback simply unavailable).
        await InitializeAudioEngineAsync();

        // Operator window: song-select remote, reservation queue, key/tempo controls.
        // Stays on the primary monitor.
        _operatorWindow = GetService<OperatorWindow>();

        // P8: surface the SoundFont path on the operator settings section. The installer
        // resolves the default path regardless of whether the file was downloaded.
        var sfInstaller = GetService<SoundFontInstaller>();
        _operatorWindow.ViewModel.SoundFontPath = sfInstaller.DefaultSoundFontPath;

        _operatorWindow.Activate();

        // Display window: lyric telop, background, real-time pitch bar. Placed on a
        // secondary monitor when one is present; otherwise shown on the primary.
        _displayWindow = GetService<DisplayWindow>();
        _displayWindow.PlaceOnSecondaryMonitor(_operatorWindow);
        _displayWindow.Activate();
    }

    /// <summary>
    /// Ensures the default SoundFont is on disk and constructs the singleton
    /// <see cref="IAudioEngine"/> using the current (UI) thread's <see cref="DispatcherQueue"/>.
    /// Any download failure is swallowed: the engine is still created (disabled) so the UI opens.
    /// Then builds the singleton <see cref="IPitchMonitor"/> (P3) against the engine and links it to
    /// the playback transport so mic capture follows play/pause/stop.
    /// </summary>
    private async System.Threading.Tasks.Task InitializeAudioEngineAsync()
    {
        var installer = GetService<SoundFontInstaller>();

        try
        {
            await installer.EnsureDefaultSoundFontAsync();
        }
        catch (Exception)
        {
            // Network/IO failure: leave the SoundFont absent. The engine detects the missing
            // file and constructs disabled; the operator UI surfaces "playback unavailable".
        }

        var dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var holder = GetService<AudioEngineHolder>();
        holder.Initialize(installer.DefaultSoundFontPath, dispatcherQueue);

        // P6: wire the corrections store and the song-number accessor into the engine so
        // BuildAndLoadSequence can load and apply corrections for the current song. The accessor
        // reads from the shared INowPlaying singleton, which the operator sets on every load.
        if (holder.Engine is MeltySynthAudioEngine concreteEngine)
        {
            var correctionsStore = GetService<ISongCorrectionsStore>();
            var nowPlaying = GetService<INowPlaying>();
            concreteEngine.SetCorrectionsSource(correctionsStore, () => nowPlaying.CurrentSong?.Number);
        }

        // P3: build the pitch monitor against the now-constructed engine, then wire it to the
        // transport. The monitor opens no device until playback starts (and degrades gracefully if
        // there is no microphone), so this never blocks startup.
        var engine = GetService<IAudioEngine>();
        var monitorHolder = GetService<PitchMonitorHolder>();
        monitorHolder.Initialize(engine, dispatcherQueue);

        _pitchTransportLink = new PitchMonitorTransportLink(engine, monitorHolder.Monitor, dispatcherQueue);
    }

    private static IHost BuildHost()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        ConfigureServices(builder.Services);

        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppInfoService, AppInfoService>();

        // Song library (REQUIREMENTS §5). Singleton: one SQLite connection is opened
        // lazily and shared. Stored under LocalApplicationData so it survives reinstalls
        // of the unpackaged app and is per-user.
        services.AddSingleton<ISongLibrary>(_ => new SqliteSongLibrary(GetLibraryDatabasePath()));

        // P5 score & history persistence (REQUIREMENTS §5). Singleton over the SAME SQLite DB file as
        // the library (its own Scores table + index, created by InitializeAsync at startup). One
        // history instance is shared so the display window's saves and best/recent lookups hit the
        // same open connection.
        services.AddSingleton<IScoreHistory>(_ => new SqliteScoreHistory(GetLibraryDatabasePath()));

        // P6 song corrections (REQUIREMENTS §6). Singleton over the SAME SQLite DB file as the
        // library (its own SongCorrections table, created by InitializeAsync at startup). Shared so
        // the operator editor and the engine read/write the same data.
        services.AddSingleton<ISongCorrectionsStore>(_ =>
            new SqliteSongCorrectionsStore(GetLibraryDatabasePath()));

        // P5 shared current-song state. Singleton so the operator (which sets it on load) and the
        // display (which reads it when a score is produced) agree on what is playing.
        services.AddSingleton<INowPlaying, NowPlaying>();

        // P1 audio + MIDI (REQUIREMENTS §4). The HttpClient powers the first-run SoundFont
        // download; SoundFontInstaller resolves/creates the default .sf2; MidiLoader parses
        // SMF/KAR into the Core model.
        services.AddSingleton<HttpClient>();
        services.AddSingleton(sp => new SoundFontInstaller(sp.GetRequiredService<HttpClient>()));
        services.AddSingleton<IMidiLoader, MidiLoader>();

        // P7 internet import (REQUIREMENTS §6). Brings MIDI/KAR into the library from local files and
        // URLs — the only sanctioned content path (no streaming-audio ripping). Reuses the shared
        // HttpClient (URL download), IMidiLoader (validate/parse), and ISongLibrary (upsert). Imported
        // files are copied under <LocalApplicationData>/Saikara/library, alongside the library DB.
        services.AddSingleton<IMidiImportService>(sp => new MidiImportService(
            sp.GetRequiredService<ISongLibrary>(),
            sp.GetRequiredService<IMidiLoader>(),
            sp.GetRequiredService<HttpClient>(),
            GetLibraryContentDirectory()));

        // The audio engine needs the UI DispatcherQueue and the resolved SoundFont path, neither
        // of which exist when the host is built. A holder is constructed at startup (OnLaunched,
        // on the UI thread) and the engine resolves through it.
        services.AddSingleton<AudioEngineHolder>();
        services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<AudioEngineHolder>().Engine);

        // P3 mic & pitch (REQUIREMENTS §6). The Core pitch detector is platform-agnostic and built
        // eagerly. The PitchMonitor (WASAPI mic capture -> detector -> live result + PitchSample
        // accumulator) is a singleton that, like the engine, needs the UI DispatcherQueue and the
        // constructed engine, so it resolves through a holder initialised in OnLaunched.
        services.AddSingleton<IPitchDetector, McLeodPitchDetector>();
        services.AddSingleton<PitchMonitorHolder>();
        services.AddSingleton<IPitchMonitor>(sp => sp.GetRequiredService<PitchMonitorHolder>().Monitor);

        // P4 scoring (REQUIREMENTS §6). The scoring engine is platform-agnostic, pure and
        // deterministic, so it is built eagerly and shared. DisplayViewModel runs it once at
        // song end against the collected mic samples and the reference melody.
        services.AddSingleton<IScoringEngine, ScoringEngine>();

        // View-models. Transient so each window instance gets its own state. The operator VM now
        // needs the corrections store (P6), pitch monitor (P8 latency), and the dispatcher queue
        // (P8 queue-advance timer).
        services.AddTransient(sp => new OperatorViewModel(
            sp.GetRequiredService<IAppInfoService>(),
            sp.GetRequiredService<ISongLibrary>(),
            sp.GetRequiredService<IAudioEngine>(),
            sp.GetRequiredService<IMidiLoader>(),
            sp.GetRequiredService<IMidiImportService>(),
            sp.GetRequiredService<ISongCorrectionsStore>(),
            sp.GetRequiredService<INowPlaying>(),
            sp.GetRequiredService<IPitchMonitor>(),
            DispatcherQueue.GetForCurrentThread()));

        // DisplayViewModel needs the UI DispatcherQueue for its ~30 fps telop/pitch-bar frame timer,
        // and the pitch monitor for the live sung pitch. It is resolved in OnLaunched on the UI
        // thread, so capturing the current thread's queue here is correct (the engine's
        // PositionChanged and the monitor's PitchDetected are marshaled to this same thread).
        services.AddTransient(sp => new DisplayViewModel(
            sp.GetRequiredService<IAudioEngine>(),
            sp.GetRequiredService<IPitchMonitor>(),
            sp.GetRequiredService<IScoringEngine>(),
            sp.GetRequiredService<IScoreHistory>(),
            sp.GetRequiredService<INowPlaying>(),
            DispatcherQueue.GetForCurrentThread()));

        // Windows. Transient: a window is consumed once at launch.
        services.AddTransient<OperatorWindow>();
        services.AddTransient<DisplayWindow>();
    }

    /// <summary>
    /// Resolves the on-disk path of the SQLite library database under the per-user
    /// LocalApplicationData folder, ensuring the containing directory exists.
    /// </summary>
    private static string GetLibraryDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Saikara");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "library.db");
    }

    /// <summary>
    /// Resolves the directory imported MIDI/KAR files are copied into, under the per-user
    /// LocalApplicationData folder (alongside the library database), ensuring it exists. This is
    /// the same default <see cref="MidiImportService"/> would compute, made explicit here so DI
    /// is self-documenting.
    /// </summary>
    private static string GetLibraryContentDirectory()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Saikara",
            "library");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
