using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CanTools.Model;

namespace CanTools.Formats.Sym;

/// <summary>Reads PEAK PCAN SYM 6.0 files into a <see cref="Database"/>.</summary>
public static partial class SymReader
{
    [GeneratedRegex("^FormatVersion=6.0", RegexOptions.Multiline)]
    private static partial Regex VersionGate();

    /// <summary>The encoding used by <see cref="LoadFile"/> when none is given (cp1252).</summary>
    public static Encoding DefaultEncoding => FormatEncodings.Cp1252;

    public static Database LoadFile(
        string path,
        Encoding? encoding = null,
        bool strict = true,
        SignalSort? sortSignals = null,
        uint frameIdMask = 0xffffffff)
    {
        var text = File.ReadAllText(path, encoding ?? DefaultEncoding);

        return LoadString(text, strict, sortSignals, frameIdMask);
    }

    public static Database LoadString(
        string text,
        bool strict = true,
        SignalSort? sortSignals = null,
        uint frameIdMask = 0xffffffff)
    {
        var database = new Database(strict: strict, sortSignals: sortSignals,
                                    frameIdMask: frameIdMask);
        database.AddSymString(text, sortSignals);

        return database;
    }

    internal static (List<Message> Messages, string? Version) Load(
        string text, bool strict, SignalSort? sortSignals)
    {
        if (!VersionGate().IsMatch(text))
        {
            throw new ParseException("Only SYM version 6.0 is supported.");
        }

        var parser = new SymParser(text);
        var contents = parser.Parse();
        var builder = new SymBuilder(contents, strict, sortSignals);

        return (builder.BuildMessages(), contents.Version);
    }
}
