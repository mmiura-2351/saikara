using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Saikara.App.Services;
using Saikara.App.ViewModels;
using Saikara.App.Views;
using Saikara.Core.Library;

namespace Saikara.App;

/// <summary>
/// Application entry point. Builds the dependency-injection host (view-models and
/// services) and, on launch, opens the two-window layout described in REQUIREMENTS §5:
/// an operator window (primary monitor) and a display window (secondary monitor).
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// The composition root. Exposed so windows/pages can resolve their view-models.
    /// </summary>
    public static IHost Host { get; private set; } = null!;

    private OperatorWindow? _operatorWindow;
    private DisplayWindow? _displayWindow;

    public App()
    {
        InitializeComponent();
        Host = BuildHost();
    }

    /// <summary>
    /// Resolves a service from the DI container. Convenience for XAML code-behind.
    /// </summary>
    public static T GetService<T>() where T : notnull => Host.Services.GetRequiredService<T>();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Create the SQLite schema before any window queries the library. The library
        // is a singleton, so this initialises the one shared instance. InitializeAsync
        // is idempotent (CREATE TABLE IF NOT EXISTS), so a repeat call is harmless.
        var library = GetService<ISongLibrary>();
        await library.InitializeAsync();

        // Operator window: song-select remote, reservation queue, key/tempo controls.
        // Stays on the primary monitor.
        _operatorWindow = GetService<OperatorWindow>();
        _operatorWindow.Activate();

        // Display window: lyric telop, background, real-time pitch bar. Placed on a
        // secondary monitor when one is present; otherwise shown on the primary.
        _displayWindow = GetService<DisplayWindow>();
        _displayWindow.PlaceOnSecondaryMonitor(_operatorWindow);
        _displayWindow.Activate();
    }

    private static IHost BuildHost()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();

        ConfigureServices(builder.Services);

        return builder.Build();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAppInfoService, AppInfoService>();

        // Song library (REQUIREMENTS §5). Singleton: one SQLite connection is opened
        // lazily and shared. Stored under LocalApplicationData so it survives reinstalls
        // of the unpackaged app and is per-user.
        services.AddSingleton<ISongLibrary>(_ => new SqliteSongLibrary(GetLibraryDatabasePath()));

        // View-models. Transient so each window instance gets its own state.
        services.AddTransient<OperatorViewModel>();
        services.AddTransient<DisplayViewModel>();

        // Windows. Transient: a window is consumed once at launch.
        services.AddTransient<OperatorWindow>();
        services.AddTransient<DisplayWindow>();
    }

    /// <summary>
    /// Resolves the on-disk path of the SQLite library database under the per-user
    /// LocalApplicationData folder, ensuring the containing directory exists.
    /// </summary>
    private static string GetLibraryDatabasePath()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Saikara");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "library.db");
    }
}
