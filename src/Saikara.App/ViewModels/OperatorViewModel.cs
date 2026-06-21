using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Saikara.App.Services;

namespace Saikara.App.ViewModels;

/// <summary>
/// View-model for the operator window: the song-select remote, the reservation
/// queue, and key/tempo controls (REQUIREMENTS §5). This is a minimal P0 placeholder
/// — the real song library, queue, and playback wiring land in later phases.
/// </summary>
public partial class OperatorViewModel : ObservableObject
{
    private readonly IAppInfoService _appInfo;

    public OperatorViewModel(IAppInfoService appInfo)
    {
        _appInfo = appInfo;
    }

    /// <summary>Application name, surfaced in the operator header.</summary>
    public string AppName => _appInfo.AppName;

    /// <summary>Free-text search term for the song-select remote (number / title / artist).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Semitone transpose applied to playback. 0 = original key.</summary>
    [ObservableProperty]
    private int _keyOffset;

    /// <summary>
    /// Tempo adjustment as a percentage of the original. 100 = original tempo.
    /// Typed as <see cref="double"/> to match <c>NumberBox.Value</c> for two-way binding.
    /// </summary>
    [ObservableProperty]
    private double _tempoPercent = 100;

    /// <summary>Placeholder reservation queue. Items are display strings for now.</summary>
    public ObservableCollection<string> ReservationQueue { get; } = new();

    /// <summary>Adds the current search text to the reservation queue (placeholder).</summary>
    [RelayCommand]
    private void AddToQueue()
    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            ReservationQueue.Add(SearchText.Trim());
            SearchText = string.Empty;
        }
    }

    /// <summary>Raises the playback key by one semitone (placeholder).</summary>
    [RelayCommand]
    private void KeyUp() => KeyOffset++;

    /// <summary>Lowers the playback key by one semitone (placeholder).</summary>
    [RelayCommand]
    private void KeyDown() => KeyOffset--;
}
