using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Saikara.App.ViewModels;
using Windows.Graphics;

namespace Saikara.App.Views;

/// <summary>
/// Operator window: song-select remote, reservation queue, and key/tempo controls
/// (REQUIREMENTS §5). Hosts <see cref="OperatorViewModel"/>.
/// </summary>
public sealed partial class OperatorWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    /// <summary>View-model bound by the XAML via <c>x:Bind</c>.</summary>
    public OperatorViewModel ViewModel { get; }

    public OperatorWindow(OperatorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Saikara — Operator";
        ResizeToContent();
    }

    /// <summary>
    /// Formats the semitone key offset for display (e.g. <c>+2</c>, <c>0</c>, <c>-1</c>).
    /// Static so it can be used directly from an <c>x:Bind</c> function binding.
    /// </summary>
    public static string FormatKey(int keyOffset) =>
        keyOffset > 0 ? $"+{keyOffset}" : keyOffset.ToString();

    /// <summary>
    /// Sizes the operator window from its layout. <see cref="AppWindow.Resize"/> takes
    /// physical pixels, so the DIP target is scaled by the monitor DPI.
    /// </summary>
    private void ResizeToContent()
    {
        var hwnd = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(760 * scale)));
    }
}
