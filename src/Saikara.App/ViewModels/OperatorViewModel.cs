using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saikara.App.Audio;
using Saikara.App.Services;
using Saikara.Core.Audio;
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

    public OperatorViewModel(
        IAppInfoService appInfo,
        ISongLibrary library,
        IAudioEngine audioEngine,
        IMidiLoader midiLoader)
    {
        _appInfo = appInfo;
        _library = library;
        _audioEngine = audioEngine;
        _midiLoader = midiLoader;

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
    /// Opens a MIDI/KAR file through the window-supplied picker and loads it into the engine.
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

        // Parse off the UI thread; the loader is pure managed code with no UI affinity.
        MidiSong song = await Task.Run(() => _midiLoader.Load(file.Path)).ConfigureAwait(true);

        _audioEngine.Load(song);

        // Reapply the operator's current key/tempo to the freshly loaded song.
        _audioEngine.SemitoneOffset = KeyOffset;
        _audioEngine.TempoPercent = TempoPercent;

        LoadedFileName = file.Name;
        IsSongLoaded = true;
        SyncDurationFromEngine();
        SyncPositionFromEngine();
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
