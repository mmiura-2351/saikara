using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Saikara.App.ViewModels;
using Saikara.Core.Library;
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
    /// Runs a library search as the user types. Only reacts to <see cref="AutoSuggestionBoxTextChangedReason.UserInput"/>
    /// so programmatic <c>Text</c> updates (e.g. clearing on add-to-queue) do not re-query.
    /// </summary>
    private async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangedReason.UserInput)
        {
            await ViewModel.SearchAsync();
        }
    }

    /// <summary>
    /// Runs a search when the query is submitted (Enter / search-glyph). If a result was
    /// chosen from the list it is queued directly; otherwise the results are refreshed.
    /// </summary>
    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is Song chosen)
        {
            ViewModel.AddSongToQueue(chosen);
            return;
        }

        await ViewModel.SearchAsync();
    }

    /// <summary>Adds the double-tapped search result to the reservation queue.</summary>
    private void OnSearchResultDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: Song song })
        {
            ViewModel.AddSongToQueue(song);
        }
    }

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
