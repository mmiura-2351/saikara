using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Saikara.App.ViewModels;
using Saikara.Core.Audio;
using Saikara.Core.Library;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Saikara.App.Views;

/// <summary>
/// Operator window: song-select remote, reservation queue, key/tempo controls, and the
/// P1 playback transport (REQUIREMENTS §4–§5). Hosts <see cref="OperatorViewModel"/>.
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

        // Supply the file-picker hook: the picker must be initialised with THIS window's HWND in
        // an unpackaged WinUI app, so we own it here and hand the chosen file back to the VM.
        ViewModel.PickMidiFileAsync = PickMidiFileAsync;

        // Supply the URL-prompt hook: a ContentDialog needs this window's XamlRoot, which only the
        // window can provide, so we own the dialog here and hand the entered URL back to the VM.
        ViewModel.PromptForImportUrlAsync = PromptForImportUrlAsync;

        Title = "Saikara — Operator";
        ResizeToContent();
    }

    /// <summary>
    /// Formats the semitone key offset for display (e.g. <c>+2</c>, <c>0</c>, <c>-1</c>).
    /// Static so it can be used directly from an <c>x:Bind</c> function binding.
    /// </summary>
    public static string FormatKey(int keyOffset) =>
        keyOffset > 0 ? $"+{keyOffset}" : keyOffset.ToString();

    /// <summary>Boolean negation for <c>x:Bind</c> (shows the "playback unavailable" banner when NOT enabled).</summary>
    public static bool Not(bool value) => !value;

    /// <summary>Whether the Play command is currently allowed (a song is loaded and not already playing).</summary>
    public static bool CanPlay(bool isSongLoaded, bool isPlaybackEnabled, PlaybackState state) =>
        isSongLoaded && isPlaybackEnabled && state != PlaybackState.Playing;

    /// <summary>Whether the Pause command is currently allowed (engine is playing).</summary>
    public static bool CanPause(PlaybackState state) => state == PlaybackState.Playing;

    /// <summary>Whether the Stop command is currently allowed (engine is not already stopped).</summary>
    public static bool CanStop(bool isSongLoaded, PlaybackState state) =>
        isSongLoaded && state != PlaybackState.Stopped;

    /// <summary>
    /// Shows the MIDI/KAR file picker, initialised with this window's HWND (the classic
    /// unpackaged-WinUI requirement), and returns the chosen file or <see langword="null"/>.
    /// </summary>
    private async Task<StorageFile?> PickMidiFileAsync()
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.MusicLibrary,
        };
        picker.FileTypeFilter.Add(".mid");
        picker.FileTypeFilter.Add(".midi");
        picker.FileTypeFilter.Add(".kar");

        // Unpackaged apps have no implicit window context for the picker; bind it to our HWND.
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFileAsync();
    }

    /// <summary>
    /// Shows a modal dialog asking for a MIDI/KAR URL and returns the trimmed entry, or
    /// <see langword="null"/> when the user cancels or leaves it blank. The dialog is anchored to
    /// this window via <c>Content.XamlRoot</c> (required for a <see cref="ContentDialog"/> in an
    /// unpackaged WinUI app).
    /// </summary>
    private async Task<string?> PromptForImportUrlAsync()
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://example.com/song.mid",
        };
        AutomationProperties.SetAutomationId(urlBox, "OperatorImportUrlTextBox");
        AutomationProperties.SetName(urlBox, "MIDI or KAR URL");

        var dialog = new ContentDialog
        {
            Title = "Import from URL",
            Content = urlBox,
            PrimaryButtonText = "Import",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        AutomationProperties.SetAutomationId(dialog, "OperatorImportUrlDialog");

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        string url = urlBox.Text.Trim();
        return url.Length == 0 ? null : url;
    }

    /// <summary>
    /// Runs a library search as the user types. Only reacts to <see cref="AutoSuggestionBoxTextChangeReason.UserInput"/>
    /// so programmatic <c>Text</c> updates (e.g. clearing on add-to-queue) do not re-query.
    /// </summary>
    private async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
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
        // Taller than the P0 layout: the left column now also carries the playback transport
        // (open-file, file label, optional banner, play/pause/stop, seek slider, time labels).
        AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(940 * scale)));
    }
}
