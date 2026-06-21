using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Saikara.App.Audio;
using Saikara.App.Services;
using Saikara.Core.Audio;
using Saikara.Core.Import;
using Saikara.Core.Library;
using Saikara.Core.Midi;
using Windows.Storage;

namespace Saikara.App.ViewModels;

/// <summary>
/// View-model for the operator window: the song-select remote, the reservation
/// queue, key/tempo controls, and the P1 playback transport (REQUIREMENTS §4–§5).
/// Search is backed by the <see cref="ISongLibrary"/>; playback is driven through the
/// platform-agnostic <see cref="IAudioEngine"/> (MeltySynth + NAudio implementation in
/// <c>Saikara.App</c>). A MIDI file is opened via <see cref="IMidiLoader"/>.
/// </summary>
public partial class OperatorViewModel : ObservableObject
{
    private readonly IAppInfoService _appInfo;
    private readonly ISongLibrary _library;
    private readonly IAudioEngine _audioEngine;
    private readonly IMidiLoader _midiLoader;
    private readonly IMidiImportService _importService;

    /// <summary>
    /// Guards <see cref="PositionSeconds"/> against feedback: when the engine's
    /// <see cref="IAudioEngine.PositionChanged"/> drives the slider we must not interpret the
    /// resulting setter call as a user seek.
    /// </summary>
    private bool _isSyncingPositionFromEngine;

    /// <summary>
    /// Optional file-picker hook supplied by the window code-behind. The picker must be
    /// initialised with the window HWND (unpackaged WinUI requirement), which only the window
    /// can provide, so the VM delegates the actual picking and receives the chosen file.
    /// </summary>
    public Func<Task<StorageFile?>>? PickMidiFileAsync { get; set; }

    /// <summary>
    /// Optional URL-prompt hook supplied by the window code-behind. A <see cref="ContentDialog"/>
    /// needs the window's <c>XamlRoot</c> (which only the window can supply), so the VM delegates
    /// the prompt and receives the entered URL (or <see langword="null"/> on cancel).
    /// </summary>
    public Func<Task<string?>>? PromptForImportUrlAsync { get; set; }

    public OperatorViewModel(
        IAppInfoService appInfo,
        ISongLibrary library,
        IAudioEngine audioEngine,
        IMidiLoader midiLoader,
        IMidiImportService importService)
    {
        _appInfo = appInfo;
        _library = library;
        _audioEngine = audioEngine;
        _midiLoader = midiLoader;
        _importService = importService;

        // Seed transport state from the engine, then follow it.
        _isPlaybackEnabled = (_audioEngine as MeltySynthAudioEngine)?.IsPlaybackEnabled ?? true;
        _audioEngine.StateChanged += OnEngineStateChanged;
        _audioEngine.PositionChanged += OnEnginePositionChanged;
    }

    /// <summary>Application name, surfaced in the operator header.</summary>
    public string AppName => _appInfo.AppName;

    /// <summary>Free-text search term for the song-select remote (number / title / artist).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>The song currently highlighted in the search-results list (may be null).</summary>
    [ObservableProperty]
    private Song? _selectedSong;

    /// <summary>
    /// The library <see cref="Song"/> currently loaded into the engine, or <see langword="null"/>
    /// for an ad-hoc "Open MIDI file…" load. Later phases (P5 score &amp; history persistence)
    /// attach the score to this song; a null value means the loaded MIDI is not a library entry.
    /// </summary>
    [ObservableProperty]
    private Song? _currentSong;

    /// <summary>Message shown in the import-status <c>InfoBar</c> after an import attempt.</summary>
    [ObservableProperty]
    private string _importMessage = string.Empty;

    /// <summary>Whether the import-status <c>InfoBar</c> is currently shown.</summary>
    [ObservableProperty]
    private bool _isImportInfoOpen;

    /// <summary>Severity of the import-status <c>InfoBar</c> (success vs. error).</summary>
    [ObservableProperty]
    private InfoBarSeverity _importSeverity = InfoBarSeverity.Informational;

    /// <summary>
    /// <see langword="true"/> while an import (file or URL) is in flight; gates the import buttons
    /// so concurrent imports cannot stack up.
    /// </summary>
    [ObservableProperty]
    private bool _isImporting;

    /// <summary>Semitone transpose applied to playback. 0 = original key.</summary>
    [ObservableProperty]
    private int _keyOffset;

    /// <summary>
    /// Tempo adjustment as a percentage of the original. 100 = original tempo.
    /// Typed as <see cref="double"/> to match <c>NumberBox.Value</c> for two-way binding.
    /// </summary>
    [ObservableProperty]
    private double _tempoPercent = 100;

    /// <summary>Name of the currently loaded MIDI file (file name only), or a prompt when none.</summary>
    [ObservableProperty]
    private string _loadedFileName = "No file loaded";

    /// <summary><see langword="true"/> once a song is loaded into the engine; gates the transport.</summary>
    [ObservableProperty]
    private bool _isSongLoaded;

    /// <summary>
    /// <see langword="false"/> when the SoundFont is missing / the audio device failed to open.
    /// The UI shows a "playback unavailable" hint and the transport controls stay disabled.
    /// </summary>
    [ObservableProperty]
    private bool _isPlaybackEnabled = true;

    /// <summary>Mirror of <see cref="IAudioEngine.State"/> for the UI (button enablement / glyphs).</summary>
    [ObservableProperty]
    private PlaybackState _playbackState = PlaybackState.Stopped;

    /// <summary>
    /// Playback position in seconds, bound two-way to the seek <c>Slider</c>. Engine-driven
    /// updates set it via <see cref="_isSyncingPositionFromEngine"/>; a user drag seeks the engine.
    /// </summary>
    [ObservableProperty]
    private double _positionSeconds;

    /// <summary>Total duration of the loaded song in seconds (the <c>Slider.Maximum</c>).</summary>
    [ObservableProperty]
    private double _durationSeconds;

    /// <summary>Elapsed time label, formatted <c>m:ss</c>.</summary>
    public string ElapsedText => FormatTime(TimeSpan.FromSeconds(PositionSeconds));

    /// <summary>Total time label, formatted <c>m:ss</c>.</summary>
    public string TotalText => FormatTime(TimeSpan.FromSeconds(DurationSeconds));

    /// <summary>
    /// Live results from the song library, refreshed by <see cref="SearchAsync"/>.
    /// The collection instance is stable (never reassigned) so the bound
    /// <c>ListView</c> keeps its <c>ItemsSource</c>; items are replaced in place.
    /// </summary>
    public ObservableCollection<Song> SearchResults { get; } = new();

    /// <summary>
    /// Reservation queue of selected songs (REQUIREMENTS §5 — multi-singer queue).
    /// Stable instance; mutated in place.
    /// </summary>
    public ObservableCollection<Song> ReservationQueue { get; } = new();

    /// <summary>
    /// Queries the library for <see cref="SearchText"/> and refreshes
    /// <see cref="SearchResults"/> in place. An empty query returns the whole library
    /// (per <see cref="ISongLibrary.SearchAsync"/>). Awaitable; never blocks the UI thread.
    /// </summary>
    public async Task SearchAsync()
    {
        var results = await _library.SearchAsync(SearchText).ConfigureAwait(true);

        SearchResults.Clear();
        foreach (var song in results)
        {
            SearchResults.Add(song);
        }
    }

    /// <summary>
    /// Adds the given song (or the current <see cref="SelectedSong"/>) to the reservation
    /// queue. Skips null and de-duplicates by <see cref="Song.Number"/>.
    /// </summary>
    public void AddSongToQueue(Song? song)
    {
        var target = song ?? SelectedSong;
        if (target is null)
        {
            return;
        }

        foreach (var queued in ReservationQueue)
        {
            if (string.Equals(queued.Number, target.Number, StringComparison.Ordinal))
            {
                return;
            }
        }

        ReservationQueue.Add(target);
    }

    /// <summary>Adds the currently selected song to the reservation queue.</summary>
    [RelayCommand]
    private void AddToQueue() => AddSongToQueue(SelectedSong);

    /// <summary>Raises the playback key by one semitone.</summary>
    [RelayCommand]
    private void KeyUp() => KeyOffset++;

    /// <summary>Lowers the playback key by one semitone.</summary>
    [RelayCommand]
    private void KeyDown() => KeyOffset--;

    /// <summary>
    /// Opens an ad-hoc MIDI/KAR file through the window-supplied picker and loads it into the
    /// engine. The loaded file is not a library entry, so <see cref="CurrentSong"/> is cleared.
    /// No-op if no picker hook is wired or the user cancels.
    /// </summary>
    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (PickMidiFileAsync is null)
        {
            return;
        }

        StorageFile? file = await PickMidiFileAsync();
        if (file is null)
        {
            return;
        }

        await LoadFileIntoEngineAsync(file.Path, file.Name, libraryEntry: null).ConfigureAwait(true);
    }

    /// <summary>
    /// Loads the currently selected library song into the engine and starts playback. The chosen
    /// library <see cref="Song"/> is tracked in <see cref="CurrentSong"/> so later phases can attach
    /// score history. No-op when nothing is selected; file-load failures are surfaced gracefully.
    /// </summary>
    [RelayCommand]
    private async Task PlaySelectedAsync()
    {
        Song? song = SelectedSong;
        if (song is null)
        {
            return;
        }

        string fileName = Path.GetFileName(song.FilePath);
        bool loaded = await LoadFileIntoEngineAsync(song.FilePath, fileName, libraryEntry: song)
            .ConfigureAwait(true);

        if (loaded && IsPlaybackEnabled)
        {
            _audioEngine.Play();
        }
    }

    /// <summary>
    /// Imports a local MIDI/KAR file into the library via the window-supplied picker, then refreshes
    /// the search results so the new song appears. Surfaces success / failure in the import InfoBar.
    /// No-op if no picker hook is wired or the user cancels.
    /// </summary>
    [RelayCommand]
    private async Task ImportFromFileAsync()
    {
        if (PickMidiFileAsync is null)
        {
            return;
        }

        StorageFile? file = await PickMidiFileAsync();
        if (file is null)
        {
            return;
        }

        await RunImportAsync(
            () => _importService.ImportFileAsync(file.Path, DateTimeOffset.Now)).ConfigureAwait(true);
    }

    /// <summary>
    /// Prompts for a URL via the window-supplied dialog, downloads and imports the MIDI/KAR there,
    /// then refreshes the search results. Surfaces success / failure in the import InfoBar. No-op if
    /// no prompt hook is wired, the user cancels, or the URL is blank.
    /// </summary>
    [RelayCommand]
    private async Task ImportFromUrlAsync()
    {
        if (PromptForImportUrlAsync is null)
        {
            return;
        }

        string? url = await PromptForImportUrlAsync();
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        await RunImportAsync(
            () => _importService.ImportUrlAsync(url!, addedAt: DateTimeOffset.Now)).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs an import operation, refreshing the search results on success and translating an
    /// <see cref="ImportException"/> (or any unexpected error) into a friendly InfoBar message.
    /// Guards against concurrent imports via <see cref="IsImporting"/>.
    /// </summary>
    private async Task RunImportAsync(Func<Task<ImportResult>> import)
    {
        if (IsImporting)
        {
            return;
        }

        IsImporting = true;
        try
        {
            ImportResult result = await import().ConfigureAwait(true);

            // Re-run the current search so the imported song appears (empty query => whole library).
            await SearchAsync().ConfigureAwait(true);

            ShowImportInfo(
                $"Imported “{result.Song.Title}”.",
                InfoBarSeverity.Success);
        }
        catch (ImportException ex)
        {
            ShowImportInfo(ex.Message, InfoBarSeverity.Error);
        }
        catch (Exception)
        {
            ShowImportInfo("The import failed unexpectedly.", InfoBarSeverity.Error);
        }
        finally
        {
            IsImporting = false;
        }
    }

    /// <summary>
    /// Loads a MIDI/KAR file from <paramref name="filePath"/> into the engine, reapplying the
    /// current key/tempo, and tracks <paramref name="libraryEntry"/> as <see cref="CurrentSong"/>.
    /// Returns <see langword="true"/> on success; a parse/IO failure is surfaced in the import
    /// InfoBar and returns <see langword="false"/> without changing the loaded state.
    /// </summary>
    private async Task<bool> LoadFileIntoEngineAsync(string filePath, string fileName, Song? libraryEntry)
    {
        MidiSong song;
        try
        {
            // Parse off the UI thread; the loader is pure managed code with no UI affinity.
            song = await Task.Run(() => _midiLoader.Load(filePath)).ConfigureAwait(true);
        }
        catch (Exception)
        {
            ShowImportInfo($"Could not load “{fileName}”.", InfoBarSeverity.Error);
            return false;
        }

        _audioEngine.Load(song);

        // Reapply the operator's current key/tempo to the freshly loaded song.
        _audioEngine.SemitoneOffset = KeyOffset;
        _audioEngine.TempoPercent = TempoPercent;

        CurrentSong = libraryEntry;
        LoadedFileName = fileName;
        IsSongLoaded = true;
        SyncDurationFromEngine();
        SyncPositionFromEngine();
        return true;
    }

    /// <summary>Opens the import-status InfoBar with the given message and severity.</summary>
    private void ShowImportInfo(string message, InfoBarSeverity severity)
    {
        ImportMessage = message;
        ImportSeverity = severity;
        IsImportInfoOpen = true;
    }

    /// <summary>Starts or resumes playback.</summary>
    [RelayCommand]
    private void Play()
    {
        if (!IsSongLoaded || !IsPlaybackEnabled)
        {
            return;
        }

        _audioEngine.Play();
    }

    /// <summary>Suspends playback, keeping the position.</summary>
    [RelayCommand]
    private void Pause() => _audioEngine.Pause();

    /// <summary>Stops playback and rewinds to the start.</summary>
    [RelayCommand]
    private void Stop() => _audioEngine.Stop();

    /// <summary>Pushes a key change to the engine when <see cref="KeyOffset"/> changes.</summary>
    partial void OnKeyOffsetChanged(int value) => _audioEngine.SemitoneOffset = value;

    /// <summary>Pushes a tempo change to the engine when <see cref="TempoPercent"/> changes.</summary>
    partial void OnTempoPercentChanged(double value)
    {
        if (value > 0.0)
        {
            _audioEngine.TempoPercent = value;
            SyncDurationFromEngine();
        }
    }

    /// <summary>Seeks the engine when the user drags the slider (engine-driven sets are ignored).</summary>
    partial void OnPositionSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(ElapsedText));

        if (_isSyncingPositionFromEngine)
        {
            return;
        }

        _audioEngine.Seek(TimeSpan.FromSeconds(value));
    }

    /// <summary>Keeps the total-time label in step with the duration.</summary>
    partial void OnDurationSecondsChanged(double value) => OnPropertyChanged(nameof(TotalText));

    private void OnEngineStateChanged(object? sender, EventArgs e)
    {
        // PositionChanged/StateChanged may arrive on a background thread per the contract, but
        // this engine raises them via its UI DispatcherQueueTimer, so direct VM updates are safe.
        PlaybackState = _audioEngine.State;
        SyncPositionFromEngine();
    }

    private void OnEnginePositionChanged(object? sender, EventArgs e) => SyncPositionFromEngine();

    private void SyncPositionFromEngine()
    {
        _isSyncingPositionFromEngine = true;
        PositionSeconds = _audioEngine.Position.TotalSeconds;
        _isSyncingPositionFromEngine = false;
    }

    private void SyncDurationFromEngine() => DurationSeconds = _audioEngine.Duration.TotalSeconds;

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero)
        {
            time = TimeSpan.Zero;
        }

        return $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
