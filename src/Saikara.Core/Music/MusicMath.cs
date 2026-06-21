namespace Saikara.Core.Music;

/// <summary>
/// Pure music-theory helpers shared by key change, guide melody, pitch detection,
/// and scoring. Everything here is platform-agnostic and deterministic so it can be
/// unit tested on any OS (including the Linux CI / dev box).
/// </summary>
public static class MusicMath
{
    /// <summary>Concert pitch of A4 (MIDI note 69) in Hz.</summary>
    public const double A4Frequency = 440.0;

    /// <summary>MIDI note number of A4.</summary>
    public const int A4MidiNote = 69;

    /// <summary>Lowest / highest valid MIDI note numbers.</summary>
    public const int MinMidiNote = 0;
    public const int MaxMidiNote = 127;

    private static readonly string[] NoteNames =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>Converts a (possibly fractional) MIDI note number to a frequency in Hz.</summary>
    public static double MidiNoteToFrequency(double midiNote)
        => A4Frequency * Math.Pow(2.0, (midiNote - A4MidiNote) / 12.0);

    /// <summary>
    /// Converts a frequency in Hz to a fractional MIDI note number.
    /// The fractional part expresses how sharp/flat the pitch is (1.0 == one semitone).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="hz"/> is not positive.</exception>
    public static double FrequencyToMidiNote(double hz)
    {
        if (hz <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(hz), hz, "Frequency must be positive.");
        return A4MidiNote + 12.0 * Math.Log2(hz / A4Frequency);
    }

    /// <summary>
    /// Signed pitch error in cents (100 cents == one semitone) of <paramref name="sungHz"/>
    /// relative to the target MIDI note. Positive means the sung pitch is sharp.
    /// </summary>
    public static double CentsError(double sungHz, int targetMidiNote)
        => (FrequencyToMidiNote(sungHz) - targetMidiNote) * 100.0;

    /// <summary>
    /// Transposes a MIDI note by a number of semitones, clamping to the valid MIDI range.
    /// This is the core of the key-change feature.
    /// </summary>
    public static int Transpose(int midiNote, int semitones)
        => Math.Clamp(midiNote + semitones, MinMidiNote, MaxMidiNote);

    /// <summary>Returns the pitch class (0-11) of a MIDI note, where 0 == C.</summary>
    public static int PitchClass(int midiNote)
        => ((midiNote % 12) + 12) % 12;

    /// <summary>
    /// Scientific pitch notation for a MIDI note, e.g. 60 -> "C4", 69 -> "A4".
    /// </summary>
    public static string NoteName(int midiNote)
    {
        int octave = (midiNote / 12) - 1;
        return NoteNames[PitchClass(midiNote)] + octave.ToString();
    }
}
