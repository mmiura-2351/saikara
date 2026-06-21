using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saikara.App.Services;
using Saikara.Core.Library;

namespace Saikara.App.ViewModels;

/// <summary>
/// View-model for the operator window: the song-select remote, the reservation
/// queue, and key/tempo controls (REQUIREMENTS §5). Search is backed by the
/// <see cref="ISongLibrary"/> from <c>Saikara.Core</c>; playback wiring lands later.
/// </summary>
public partial class OperatorViewModel : ObservableObject
{
    private readonly IAppInfoService _appInfo;
    private readonly ISongLibrary _library;

    public OperatorViewModel(IAppInfoService appInfo, ISongLibrary library)
    {
        _appInfo = appInfo;
        _library = library;
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
            if (string.Equals(queued.Number, target.Number, System.StringComparison.Ordinal))
            {
                return;
            }
        }

        ReservationQueue.Add(target);
    }

    /// <summary>Adds the currently selected song to the reservation queue.</summary>
    [RelayCommand]
    private void AddToQueue() => AddSongToQueue(SelectedSong);

    /// <summary>Raises the playback key by one semitone (placeholder).</summary>
    [RelayCommand]
    private void KeyUp() => KeyOffset++;

    /// <summary>Lowers the playback key by one semitone (placeholder).</summary>
    [RelayCommand]
    private void KeyDown() => KeyOffset--;
}
