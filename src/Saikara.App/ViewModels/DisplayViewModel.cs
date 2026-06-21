using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Saikara.App.Audio;
using Saikara.Core.Audio;
using Saikara.Core.Lyrics;

namespace Saikara.App.ViewModels;

/// <summary>
/// View-model for the display window: the two-line color-wipe lyric telop synced to playback, plus
/// the (P3) pitch bar (REQUIREMENTS §5).
/// </summary>
/// <remarks>
/// <para>
/// Sourcing: the telop lines come from the App audio engine via <see cref="ITelopSource"/> — built
/// from the tempo-transformed song so each syllable's start-time matches the engine clock. The VM
/// injects the platform-agnostic <see cref="IAudioEngine"/> and tries to cast it to
/// <see cref="ITelopSource"/>; if the cast fails (e.g. a future non-telop engine) the telop simply
/// stays in its empty/instrumental state and the window still renders.
/// </para>
/// <para>
/// Sync: a ~30 fps <see cref="DispatcherQueueTimer"/> samples <see cref="ITelopSource.Position"/> and
/// recomputes the bound properties through a pure <see cref="TelopPlayback"/>. The timer runs only
/// on the UI thread, so every property update is already marshaled. <see cref="TelopChanged"/>
/// rebuilds the <see cref="TelopPlayback"/> when a new song loads or the tempo rescales the lyric
/// timeline.
/// </para>
/// </remarks>
public partial class DisplayViewModel : ObservableObject, IDisposable
{
    /// <summary>Telop refresh rate. 30 fps is smooth for a color wipe without burning the UI thread.</summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 30.0);

    private readonly ITelopSource? _telopSource;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer? _frameTimer;

    private TelopPlayback _playback = new(Array.Empty<TelopLine>());
    private bool _disposed;

    /// <summary>
    /// Shown for the current line when the song has no lyrics (instrumental) or none have started.
    /// </summary>
    private const string InstrumentalPlaceholder = "♪";

    public DisplayViewModel(IAudioEngine audioEngine, DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(audioEngine);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _dispatcherQueue = dispatcherQueue;
        _telopSource = audioEngine as ITelopSource;

        if (_telopSource is not null)
        {
            _telopSource.TelopChanged += OnTelopChanged;
            RebuildPlayback();

            // A ~30 fps timer is the wipe's clock. Position alone (engine PositionChanged, ~20 Hz)
            // would also work, but a dedicated frame timer decouples wipe smoothness from the
            // engine's coarser position cadence and keeps the gradient gliding between ticks.
            _frameTimer = _dispatcherQueue.CreateTimer();
            _frameTimer.Interval = FrameInterval;
            _frameTimer.Tick += OnFrameTick;
            _frameTimer.Start();
        }
    }

    /// <summary>The text of the current (upper) telop line. Empty-state shows an instrumental marker.</summary>
    [ObservableProperty]
    private string _currentLineText = InstrumentalPlaceholder;

    /// <summary>The text of the next (lower) telop line, shown dimmer and un-wiped. Empty when none.</summary>
    [ObservableProperty]
    private string _nextLineText = string.Empty;

    /// <summary>
    /// How far the color-wipe has swept across the current line, in <c>[0, 1]</c>. The view maps
    /// this to the shared offset of the two coincident gradient stops on the current line's brush.
    /// </summary>
    [ObservableProperty]
    private double _wipeFraction;

    /// <summary>
    /// <see langword="true"/> when the current line is real lyric text (a wipe should run);
    /// <see langword="false"/> for the instrumental placeholder (no wipe, full bright color).
    /// </summary>
    [ObservableProperty]
    private bool _hasCurrentLyric;

    /// <summary>
    /// Normalised pitch position [0..1] for the real-time pitch bar. Driven by the Core pitch
    /// detector in P3; static for now.
    /// </summary>
    [ObservableProperty]
    private double _pitchPosition = 0.5;

    /// <summary>
    /// Rebuilds the <see cref="TelopPlayback"/> from the source's current lines and refreshes the
    /// view immediately so a freshly loaded song shows its first line before playback starts.
    /// </summary>
    private void RebuildPlayback()
    {
        _playback = new TelopPlayback(_telopSource!.CurrentTelopLines);
        UpdateFromPosition();
    }

    private void OnTelopChanged(object? sender, EventArgs e)
    {
        // ITelopSource raises this on the UI thread, but marshal defensively to be safe.
        if (_dispatcherQueue.HasThreadAccess)
        {
            RebuildPlayback();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(RebuildPlayback);
        }
    }

    private void OnFrameTick(DispatcherQueueTimer sender, object args) => UpdateFromPosition();

    /// <summary>
    /// Samples the playback position and recomputes the bound telop properties. Runs on the UI
    /// thread (frame timer / dispatcher), so the property sets are already marshaled.
    /// </summary>
    private void UpdateFromPosition()
    {
        if (_telopSource is null)
        {
            return;
        }

        TimeSpan position = _telopSource.Position;
        var lines = _playback.Lines;

        if (lines.Count == 0)
        {
            // No lyrics at all: instrumental placeholder, no wipe.
            SetTelop(InstrumentalPlaceholder, string.Empty, 0.0, hasLyric: false);
            return;
        }

        int active = _playback.ActiveLineIndex(position);
        if (active < 0)
        {
            // Before the first line: preview line 0 (current, un-wiped) and line 1 (next).
            SetTelop(
                lines[0].Text,
                lines.Count > 1 ? lines[1].Text : string.Empty,
                fraction: 0.0,
                hasLyric: true);
            return;
        }

        string current = lines[active].Text;
        string next = active + 1 < lines.Count ? lines[active + 1].Text : string.Empty;
        SetTelop(current, next, _playback.WipeFraction(position), hasLyric: true);
    }

    private void SetTelop(string current, string next, double fraction, bool hasLyric)
    {
        CurrentLineText = current;
        NextLineText = next;
        WipeFraction = fraction;
        HasCurrentLyric = hasLyric;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_frameTimer is not null)
        {
            _frameTimer.Stop();
            _frameTimer.Tick -= OnFrameTick;
        }

        if (_telopSource is not null)
        {
            _telopSource.TelopChanged -= OnTelopChanged;
        }
    }
}
