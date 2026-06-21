namespace Saikara.Core.Import;

/// <summary>
/// Thrown when an import fails because the source is not a valid Standard MIDI File / KAR file,
/// or could not be retrieved. When this is thrown, the import service guarantees that no
/// <see cref="Library.Song"/> was added and no partial file was left in the library directory.
/// </summary>
public sealed class ImportException : Exception
{
    /// <summary>Creates an <see cref="ImportException"/> with a message.</summary>
    /// <param name="message">A human-readable description of why the import failed.</param>
    public ImportException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an <see cref="ImportException"/> wrapping an underlying cause.</summary>
    /// <param name="message">A human-readable description of why the import failed.</param>
    /// <param name="innerException">The underlying error (e.g. a MIDI parse or HTTP failure).</param>
    public ImportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
