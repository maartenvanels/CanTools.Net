using System.Text;
using CanTools.Model;

namespace CanTools.Formats.Dbc;

/// <summary>Reads DBC files into a <see cref="Database"/>.</summary>
public static class DbcReader
{
    static DbcReader()
    {
        // cp1252, the customary DBC encoding, needs the code-pages provider on .NET.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>The encoding used by <see cref="LoadFile"/> when none is given.</summary>
    public static Encoding DefaultEncoding =>
        Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback,
                             new DecoderReplacementFallback("�"));

    public static Database LoadFile(
        string path,
        Encoding? encoding = null,
        bool strict = true,
        bool pruneChoices = false,
        SignalSort? sortSignals = null)
    {
        var text = File.ReadAllText(path, encoding ?? DefaultEncoding);

        return LoadString(text, strict, pruneChoices, sortSignals);
    }

    public static Database LoadString(
        string text,
        bool strict = true,
        bool pruneChoices = false,
        SignalSort? sortSignals = null)
    {
        var database = new Database(strict: strict);
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
