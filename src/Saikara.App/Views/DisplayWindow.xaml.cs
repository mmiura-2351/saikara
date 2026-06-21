using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Saikara.App.ViewModels;
using Windows.Graphics;

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

    /// <summary>View-model bound by the XAML via <c>x:Bind</c>.</summary>
    public DisplayViewModel ViewModel { get; }

    public DisplayWindow(DisplayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Saikara — Display";
        ResizeToContent();
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
