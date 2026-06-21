using CommunityToolkit.Mvvm.ComponentModel;

namespace Saikara.App.ViewModels;

/// <summary>
/// View-model for the display window: the lyric telop, background, and real-time
/// pitch bar (REQUIREMENTS §5). Minimal P0 placeholder — color-wipe telop and the
/// live pitch visualiser are wired up in P2/P3.
/// </summary>
public partial class DisplayViewModel : ObservableObject
{
    /// <summary>Current (upper) telop line. Placeholder text until lyric sync lands.</summary>
    [ObservableProperty]
    private string _currentLyricLine = "Saikara";

    /// <summary>Next (lower) telop line, shown ahead of the current line.</summary>
    [ObservableProperty]
    private string _nextLyricLine = "Ready.";

    /// <summary>
    /// Normalised pitch position [0..1] for the real-time pitch bar.
    /// Driven by the Core pitch detector in P3; static for now.
    /// </summary>
    [ObservableProperty]
    private double _pitchPosition = 0.5;
}
