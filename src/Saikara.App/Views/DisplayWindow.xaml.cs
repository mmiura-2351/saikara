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
/// Intended for a secondary monitor; explicit multi-monitor placement is wired up
/// later in P0. Hosts <see cref="DisplayViewModel"/>.
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
