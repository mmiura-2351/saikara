namespace Saikara.App.Services;

/// <summary>
/// Placeholder application-info service. Exists so the DI host has a registered
/// dependency from day one; real services (audio, synthesis, library) replace and
/// join it in later phases. See ROADMAP.
/// </summary>
public interface IAppInfoService
{
    /// <summary>Human-readable application name shown in window chrome.</summary>
    string AppName { get; }
}
