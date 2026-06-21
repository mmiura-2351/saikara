using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Saikara.App.ViewModels;
using Windows.Graphics;
using Windows.UI;

namespace Saikara.App.Views;

/// <summary>
/// Display window: lyric telop, background, and real-time pitch bar (REQUIREMENTS §5).
/// Targets a secondary monitor via <see cref="PlaceOnSecondaryMonitor"/>; falls back to
/// the primary monitor on a single-monitor setup. Hosts <see cref="DisplayViewModel"/>.
/// </summary>
public sealed partial class DisplayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>The "already sung" highlight color: a fixed bright karaoke gold.</summary>
    private static readonly Color SungColor = Color.FromArgb(0xFF, 0xFF, 0xD5, 0x4A);

    /// <summary>The "not yet sung" base color: near-white.</summary>
    private static readonly Color UnsungColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>Flat fill for the instrumental placeholder (no wipe): dim white.</summary>
    private static readonly Color InstrumentalColor = Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF);

    /// <summary>View-model bound by the XAML via <c>x:Bind</c>.</summary>
    public DisplayViewModel ViewModel { get; }

    public DisplayWindow(DisplayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Saikara — Display";
        ResizeToContent();

        // The gradient color-wipe is driven imperatively: GradientStop.Offset / .Color cannot be
        // x:Bound cleanly, so the two coincident stops on the current line are updated whenever the
        // VM's WipeFraction / HasCurrentLyric change. Closed is the place to detach.
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += OnWindowClosed;

        // Seed the brush to the VM's initial state (instrumental placeholder before any song loads).
        ApplyWipe();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DisplayViewModel.WipeFraction)
            or nameof(DisplayViewModel.HasCurrentLyric))
        {
            ApplyWipe();
        }
    }

    /// <summary>
    /// Moves the two coincident gradient stops to <see cref="DisplayViewModel.WipeFraction"/> so the
    /// boundary between the sung and unsung colors tracks playback. For the instrumental placeholder
    /// both stops collapse to a flat dim fill so no wipe shows. Runs on the UI thread (the VM raises
    /// PropertyChanged from its UI-thread frame timer).
    /// </summary>
    private void ApplyWipe()
    {
        if (!ViewModel.HasCurrentLyric)
        {
            SungStop.Color = InstrumentalColor;
            UnsungStop.Color = InstrumentalColor;
            SungStop.Offset = 1.0;
            UnsungStop.Offset = 1.0;
            return;
        }

        double fraction = ViewModel.WipeFraction;
        if (double.IsNaN(fraction))
        {
            fraction = 0.0;
        }

        fraction = Math.Clamp(fraction, 0.0, 1.0);

        SungStop.Color = SungColor;
        UnsungStop.Color = UnsungColor;

        // Two stops at the same offset form a hard wipe edge: sung color up to the offset, unsung
        // after it.
        SungStop.Offset = fraction;
        UnsungStop.Offset = fraction;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        Closed -= OnWindowClosed;
        ViewModel.Dispose();
    }

    /// <summary>
    /// Places this window on a monitor other than the one hosting <paramref name="operatorWindow"/>
    /// when a second monitor exists (REQUIREMENTS §5 — dual output). The window is moved to
    /// fill the secondary monitor's work area and switched to a full-screen presenter. On a
    /// single-monitor setup the window is left on the primary monitor at its current size.
    /// </summary>
    /// <param name="operatorWindow">The operator window, used to identify the primary monitor.</param>
    public void PlaceOnSecondaryMonitor(Window operatorWindow)
    {
        var displayAreas = DisplayArea.FindAll();
        if (displayAreas.Count < 2)
        {
            // Single monitor: leave the display window on the primary monitor as-is.
            return;
        }

        // The monitor the operator window currently sits on; we want a different one.
        var operatorArea = DisplayArea.GetFromWindowId(
            operatorWindow.AppWindow.Id,
            DisplayAreaFallback.Primary);

        DisplayArea? target = null;
        foreach (var area in displayAreas)
        {
            if (area.DisplayId.Value != operatorArea.DisplayId.Value)
            {
                target = area;
                break;
            }
        }

        if (target is null)
        {
            // Defensive: all areas resolved to the operator's monitor — stay put.
            return;
        }

        // Move into the secondary monitor's work area (excludes the taskbar), then go
        // full-screen so the telop fills the output.
        AppWindow.MoveAndResize(target.WorkArea);
        AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
    }

    /// <summary>
    /// Sizes the display window from its layout. <see cref="AppWindow.Resize"/> takes
    /// physical pixels, so the DIP target is scaled by the monitor DPI.
    /// </summary>
    private void ResizeToContent()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1280 * scale), (int)(720 * scale)));
    }
}
