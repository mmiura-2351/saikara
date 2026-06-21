namespace Saikara.Core.Midi;

/// <summary>
/// A parsed Standard MIDI File (incl. <c>.kar</c> / Yamaha XF), reduced to the
/// platform-agnostic model the rest of Saikara consumes: tracks with their notes,
/// the tempo map, the lyric/text stream and the overall duration. It carries no audio
/// or synthesis state; the audio layer (MeltySynth + NAudio in <c>Saikara.App</c>)
/// reads this model to render sound, and the key/tempo transforms produce new
/// <see cref="MidiSong"/> instances.
/// </summary>
public sealed record MidiSong
{
    /// <summary>
    /// Pulses (ticks) per quarter note — the file's time division. Ticks in this model are
    /// expressed against this resolution. Saikara only supports tick-based (metrical) time
    /// division, which is what KAR/XF karaoke files use.
    /// </summary>
    public required short TicksPerQuarterNote { get; init; }

    /// <summary>
    /// Total playable duration of the song as metric (wall-clock) time, derived from the
    /// last note/event end and the tempo map.
    /// </summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// The tempo map as an ordered list of tempo changes. Always non-empty: the first entry
    /// is the initial tempo at time zero (defaulting to 120 BPM if the file sets none).
    /// </summary>
    public required IReadOnlyList<TempoChange> TempoChanges { get; init; }

    /// <summary>The song's tracks, in file order.</summary>
    public required IReadOnlyList<MidiTrack> Tracks { get; init; }

    /// <summary>
    /// All lyric/text meta events across the file, ordered by time. Source for the lyric
    /// telop (P2). Empty when the file carries no text.
    /// </summary>
    public required IReadOnlyList<LyricEvent> Lyrics { get; init; }

    /// <summary>The initial tempo (the first entry of <see cref="TempoChanges"/>).</summary>
    public TempoChange InitialTempo => TempoChanges[0];

    /// <summary>The initial tempo in beats (quarter notes) per minute.</summary>
    public double InitialBeatsPerMinute => InitialTempo.BeatsPerMinute;
}
