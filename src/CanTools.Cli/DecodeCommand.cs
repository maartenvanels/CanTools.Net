using CanTools.Formats;
using CanTools.Logs;

namespace CanTools.Cli;

/// <summary>
/// The decode subcommand: read candump/PCAN log lines from stdin and print each
/// frame with its decoded signals appended.
/// </summary>
internal static class DecodeCommand
{
    public static void Run(IReadOnlyList<string> args, TextReader stdin, TextWriter stdout)
    {
        var noDecodeChoices = false;
        var singleLine = false;
        string? encodingName = null;
        var prune = false;
        var noStrict = false;
        var frameIdMask = 0xffffffffu;
        string? databasePath = null;
        var expanded = Args.Expand(args);

        for (var i = 0; i < expanded.Count; i++)
        {
            switch (expanded[i])
            {
                case "-c" or "--no-decode-choices":
                    noDecodeChoices = true;
                    break;
                case "-s" or "--single-line":
                    singleLine = true;
                    break;
                case "-e" or "--encoding":
                    encodingName = Args.Value(expanded, ref i, expanded[i]);
                    break;
                case "--prune":
                    prune = true;
                    break;
                case "--no-strict":
                    noStrict = true;
                    break;
                case "-m" or "--frame-id-mask":
                    frameIdMask = ParseFrameIdMask(Args.Value(expanded, ref i, expanded[i]));
                    break;
                case var option when option.StartsWith('-'):
                    throw Args.Unknown(option);
                case var positional when databasePath is null:
                    databasePath = positional;
                    break;
                case var extra:
                    throw Args.Unknown(extra);
            }
        }

        if (databasePath is null)
        {
            throw new CommandLineException("the following arguments are required: database");
        }

        var database = DatabaseLoader.LoadFile(
            databasePath,
            encoding: encodingName is null ? null : TextEncodings.Resolve(encodingName),
            strict: !noStrict,
            pruneChoices: prune,
            frameIdMask: frameIdMask);

        foreach (var (line, entry) in new LogParser(stdin).ReadLines(keepUnknowns: true))
        {
            // upstream: --no-strict also allows truncated/excess data while decoding
            stdout.WriteLine(entry is null
                ? line
                : line + " ::" + MessageFormatter.FormatByFrameId(
                    database, entry.FrameId, entry.Data,
                    decodeChoices: !noDecodeChoices,
                    singleLine: singleLine,
                    allowTruncated: noStrict,
                    allowExcess: noStrict));
        }
    }

    private static uint ParseFrameIdMask(string text)
    {
        try
        {
            // the upstream option accepts any Python integer literal
            return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(text[2..], 16)
                : text.StartsWith("0o", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(text[2..], 8)
                : text.StartsWith("0b", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt32(text[2..], 2)
                : Convert.ToUInt32(text, 10);
        }
        catch (FormatException)
        {
            throw new CommandLineException($"argument -m/--frame-id-mask: invalid value: '{text}'");
        }
        catch (OverflowException)
        {
            throw new CommandLineException($"argument -m/--frame-id-mask: invalid value: '{text}'");
        }
    }
}
