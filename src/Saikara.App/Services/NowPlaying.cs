using Saikara.Core.Library;

namespace Saikara.App.Services;

/// <summary>
/// Default <see cref="INowPlaying"/>: a trivial in-memory holder of the current song. Registered as
/// a singleton so the operator and display view-models share one instance (see
/// <see cref="App.ConfigureServices"/>).
/// </summary>
public sealed class NowPlaying : INowPlaying
{
    /// <inheritdoc />
    public Song? CurrentSong { get; set; }
}
