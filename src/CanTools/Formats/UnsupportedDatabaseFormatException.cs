namespace CanTools.Formats;

/// <summary>
/// Thrown when <see cref="DatabaseLoader"/> cannot parse the input in any of the
/// attempted formats. The per-format errors say why each attempt failed.
/// </summary>
public class UnsupportedDatabaseFormatException : CanToolsException
{
    public UnsupportedDatabaseFormatException(
        Exception? dbcError, Exception? kcdError, Exception? symError)
        : base(BuildMessage(dbcError, kcdError, symError))
    {
        DbcError = dbcError;
        KcdError = kcdError;
        SymError = symError;
    }

    public Exception? DbcError { get; }

    public Exception? KcdError { get; }

    public Exception? SymError { get; }

    // matches upstream's UnsupportedDatabaseFormatError message
    private static string BuildMessage(
        Exception? dbcError, Exception? kcdError, Exception? symError)
    {
        var chunks = new List<string>(3);

        if (dbcError is not null)
        {
            chunks.Add($"DBC: \"{dbcError.Message}\"");
        }

        if (kcdError is not null)
        {
            chunks.Add($"KCD: \"{kcdError.Message}\"");
        }

        if (symError is not null)
        {
            chunks.Add($"SYM: \"{symError.Message}\"");
        }

        return string.Join(", ", chunks);
    }
}
