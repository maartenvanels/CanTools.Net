using System.Text;
using CanTools.Formats.Dbc;
using CanTools.Formats.Kcd;
using CanTools.Formats.Sym;
using CanTools.Model;

namespace CanTools.Formats;

/// <summary>
/// Loads a database without naming the format: <see cref="LoadFile"/> picks it
/// from the file extension, and when that is inconclusive the content is probed
/// against every supported format, like upstream's load_file/load_string.
/// </summary>
public static class DatabaseLoader
{
    public static Database LoadFile(
        string path,
        DatabaseFormat? format = null,
        Encoding? encoding = null,
        bool strict = true,
        bool pruneChoices = false,
        SignalSort? sortSignals = null)
    {
        format ??= Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".dbc" => DatabaseFormat.Dbc,
            ".kcd" => DatabaseFormat.Kcd,
            ".sym" => DatabaseFormat.Sym,
            _ => null,
        };

        encoding ??= format is DatabaseFormat.Dbc or DatabaseFormat.Sym
            ? DbcReader.DefaultEncoding
            : Encoding.UTF8;

        return LoadString(File.ReadAllText(path, encoding), format, strict, pruneChoices, sortSignals);
    }

    public static Database LoadString(
        string text,
        DatabaseFormat? format = null,
        bool strict = true,
        bool pruneChoices = false,
        SignalSort? sortSignals = null)
    {
        Exception? dbcError = null;
        Exception? kcdError = null;
        Exception? symError = null;

        if (format is null or DatabaseFormat.Dbc)
        {
            try
            {
                return Finish(DbcReader.LoadString(text, strict, sortSignals: sortSignals), pruneChoices);
            }
            catch (Exception exception)
            {
                dbcError = exception;
            }
        }

        if (format is null or DatabaseFormat.Kcd)
        {
            try
            {
                return Finish(KcdReader.LoadString(text, strict, sortSignals), pruneChoices);
            }
            catch (Exception exception)
            {
                kcdError = exception;
            }
        }

        if (format is null or DatabaseFormat.Sym)
        {
            try
            {
                return Finish(SymReader.LoadString(text, strict, sortSignals), pruneChoices);
            }
            catch (Exception exception)
            {
                symError = exception;
            }
        }

        throw new UnsupportedDatabaseFormatException(dbcError, kcdError, symError);
    }

    private static Database Finish(Database database, bool pruneChoices)
    {
        if (pruneChoices)
        {
            ChoicePruning.PruneDatabaseChoices(database);
        }

        return database;
    }
}
