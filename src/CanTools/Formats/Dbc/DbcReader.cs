using System.Text;
using CanTools.Model;

namespace CanTools.Formats.Dbc;

/// <summary>Reads DBC files into a <see cref="Database"/>.</summary>
public static class DbcReader
{
    /// <summary>The encoding used by <see cref="LoadFile"/> when none is given (cp1252).</summary>
    public static Encoding DefaultEncoding => FormatEncodings.Cp1252;

    public static Database LoadFile(
        string path,
        Encoding? encoding = null,
        bool strict = true,
        bool pruneChoices = false,
        SignalSort? sortSignals = null,
        uint frameIdMask = 0xffffffff)
    {
        var text = File.ReadAllText(path, encoding ?? DefaultEncoding);

        return LoadString(text, strict, pruneChoices, sortSignals, frameIdMask);
    }

    public static Database LoadString(
        string text,
        bool strict = true,
        bool pruneChoices = false,
        SignalSort? sortSignals = null,
        uint frameIdMask = 0xffffffff)
    {
        var database = new Database(strict: strict, frameIdMask: frameIdMask);
        database.AddDbcString(text, sortSignals);

        if (pruneChoices)
        {
            ChoicePruning.PruneDatabaseChoices(database);
        }

        return database;
    }

    internal static (List<Message> Messages, List<Node> Nodes, List<Bus> Buses,
                     string? Version, DbcSpecifics Dbc)
        Load(string text, bool strict, SignalSort? sortSignals)
    {
        var contents = DbcParser.Parse(text);

        return DbcBuilder.Build(contents, strict, sortSignals);
    }
}
