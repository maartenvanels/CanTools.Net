using CanTools.Formats;
using CanTools.Model;

namespace CanTools.Cli;

/// <summary>
/// The dump subcommand: pretty-print every message of a database with its layout,
/// signal tree and signal choices. Container messages and diagnostics databases
/// are not ported.
/// </summary>
internal static class DumpCommand
{
    /// <summary>Test seam, mirroring upstream's faked screen width in test_command_line.py.</summary>
    internal static Func<int>? ConsoleWidthOverride { get; set; }

    public static void Run(IReadOnlyList<string> args, TextWriter stdout)
    {
        string? encodingName = null;
        var prune = false;
        var noStrict = false;
        var withComments = false;
        string? messageName = null;
        string? databasePath = null;
        var expanded = Args.Expand(args);

        for (var i = 0; i < expanded.Count; i++)
        {
            switch (expanded[i])
            {
                case "-e" or "--encoding":
                    encodingName = Args.Value(expanded, ref i, expanded[i]);
                    break;
                case "--prune":
                    prune = true;
                    break;
                case "--no-strict":
                    noStrict = true;
                    break;
                case "--with-comments":
                    withComments = true;
                    break;
                case "-m" or "--message":
                    messageName = Args.Value(expanded, ref i, expanded[i]);
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
            pruneChoices: prune);

        IReadOnlyList<Message> messages = messageName is null
            ? database.Messages
            : database.TryGetMessageByName(messageName, out var message)
                ? [message]
                : throw new CommandLineException($"Unknown message {messageName}");

        var width = ConsoleWidthOverride?.Invoke() ?? TerminalWidth();

        stdout.WriteLine("================================= Messages =================================");
        stdout.WriteLine();
        stdout.WriteLine("  " + new string('-', 72));

        foreach (var current in messages)
        {
            DumpMessage(current, withComments, width, stdout);
        }
    }

    private static void DumpMessage(
        Message message, bool withComments, int width, TextWriter stdout)
    {
        stdout.WriteLine();
        stdout.WriteLine($"  Name:           {message.Name}");
        stdout.WriteLine($"  Id:             0x{message.FrameId:x}");

        if (message.Protocol == "j1939")
        {
            PrintJ1939FrameId(message.FrameId, stdout);
        }

        stdout.WriteLine($"  Length:         {message.Length} bytes");
        stdout.WriteLine($"  Cycle time:     {message.CycleTime?.ToString() ?? "-"} ms");
        stdout.WriteLine($"  Senders:        {FormatAnd(message.Senders)}");
        stdout.WriteLine("  Layout:");
        stdout.WriteLine();
        WriteIndented(LayoutFormatter.LayoutString(message), stdout);
        stdout.WriteLine();
        stdout.WriteLine("  Signal tree:");
        stdout.WriteLine();
        WriteIndented(SignalTreeFormatter.SignalTreeString(message, width, withComments), stdout);
        stdout.WriteLine();

        var choices = SignalChoicesString(message);

        if (choices.Length > 0)
        {
            stdout.WriteLine("  Signal choices:");
            WriteIndented(choices, stdout);
            stdout.WriteLine();
        }

        stdout.WriteLine("  " + new string('-', 72));
    }

    private static void PrintJ1939FrameId(uint frameId, TextWriter stdout)
    {
        var unpacked = J1939.FrameIdUnpack(frameId);

        stdout.WriteLine($"      Priority:       {unpacked.Priority}");

        string pduFormat, destination;
        int pduSpecific;

        if (J1939.IsPduFormat1(unpacked.PduFormat))
        {
            pduFormat = "PDU 1";
            pduSpecific = 0;
            destination = $"0x{unpacked.PduSpecific:x2}";
        }
        else
        {
            pduFormat = "PDU 2";
            pduSpecific = unpacked.PduSpecific;
            destination = "All";
        }

        var pgn = J1939.PgnPack(unpacked.Reserved, unpacked.DataPage, unpacked.PduFormat,
                                pduSpecific);

        stdout.WriteLine($"      PGN:            0x{pgn:x5}");
        stdout.WriteLine($"      Source:         0x{unpacked.SourceAddress:x2}");
        stdout.WriteLine($"      Destination:    {destination}");
        stdout.WriteLine($"      Format:         {pduFormat}");
    }

    private static string SignalChoicesString(Message message)
    {
        var lines = new List<string>();

        foreach (var signal in message.Signals)
        {
            if (signal.Choices is { Count: > 0 } choices)
            {
                lines.Add("");
                lines.Add(signal.Name);
                lines.AddRange(choices
                    .OrderBy(choice => choice.Key)
                    .Select(choice => $"    {choice.Key} {choice.Value.Name}"));
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>Joins like utils.format_and; "-" for an empty list, like the dump subparser.</summary>
    private static string FormatAnd(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "-",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    private static void WriteIndented(string block, TextWriter stdout)
    {
        foreach (var line in block.Split('\n'))
        {
            stdout.WriteLine(("    " + line).TrimEnd());
        }
    }

    private static int TerminalWidth()
    {
        try
        {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        }
        catch (IOException)
        {
            return 80;
        }
    }
}
