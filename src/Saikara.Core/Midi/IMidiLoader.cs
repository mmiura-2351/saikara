namespace Saikara.Core.Midi;

/// <summary>
/// Loads a Standard MIDI File (incl. <c>.kar</c> and Yamaha XF — same SMF container,
/// lyrics in <c>Lyric</c>/<c>Text</c> meta events) into the platform-agnostic
/// <see cref="MidiSong"/> model. Implementations are pure parsers with no audio or
/// Windows dependencies, so they run on the Linux dev/CI box.
/// </summary>
public interface IMidiLoader
{
    /// <summary>Loads a song from a file on disk.</summary>
    /// <param name="filePath">Absolute or relative path to the SMF/KAR file.</param>
    /// <returns>The parsed <see cref="MidiSong"/>.</returns>
    MidiSong Load(string filePath);

    /// <summary>Loads a song from a stream positioned at the start of an SMF/KAR file.</summary>
    /// <param name="stream">A readable stream of the file contents. Not disposed by this method.</param>
    /// <returns>The parsed <see cref="MidiSong"/>.</returns>
    MidiSong Load(Stream stream);
}
