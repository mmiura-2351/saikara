using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Saikara.App.Services;
using Saikara.App.ViewModels;
using Saikara.App.Views;

namespace Saikara.App;

/// <summary>
/// Application entry point. Builds the dependency-injection host (view-models and
/// placeholder services) and, on launch, opens the two-window layout described in
/// REQUIREMENTS §5: an operator window and a display window (secondary monitor).
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

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Operator window: song-select remote, reservation queue, key/tempo controls.
        _operatorWindow = GetService<OperatorWindow>();
        _operatorWindow.Activate();

        // Display window: lyric telop, background, real-time pitch bar.
        // Intended for a secondary monitor; multi-monitor placement lands later in P0.
        _displayWindow = GetService<DisplayWindow>();
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
        // Placeholder application services. Concrete implementations (audio I/O,
        // synthesis, library/SQLite) land in later phases; see ROADMAP.
        services.AddSingleton<IAppInfoService, AppInfoService>();

        // View-models. Transient so each window instance gets its own state.
        services.AddTransient<OperatorViewModel>();
        services.AddTransient<DisplayViewModel>();

        // Windows. Transient: a window is consumed once at launch.
        services.AddTransient<OperatorWindow>();
        services.AddTransient<DisplayWindow>();
    }
}
