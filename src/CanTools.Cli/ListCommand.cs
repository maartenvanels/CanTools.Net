using CanTools.Formats;
using CanTools.Model;

namespace CanTools.Cli;

/// <summary>
/// The list subcommand: print the buses, nodes or messages of a database file in a
/// shell-script friendly format. AUTOSAR-specific properties are not ported.
/// </summary>
internal static class ListCommand
{
    public static void Run(IReadOnlyList<string> args, TextWriter stdout)
    {
        var excludeNormal = false;
        var excludeExtended = false;
        var skipFormatSpecifics = false;
        var printAll = false;
        var printBuses = false;
        var printNodes = false;
        var prune = false;
        var noStrict = false;
        var positionals = new List<string>();
        var expanded = Args.Expand(args);

        for (var i = 0; i < expanded.Count; i++)
        {
            switch (expanded[i])
            {
                case "-n" or "--exclude-normal":
                    excludeNormal = true;
                    break;
                case "-x" or "--exclude-extended":
                    excludeExtended = true;
                    break;
                case "--skip-format-specifics":
                    skipFormatSpecifics = true;
                    break;
                case "-a" or "--all":
                    printAll = true;
                    break;
                case "-b" or "--buses":
                    printBuses = true;
                    break;
                case "-c" or "--nodes":
                    printNodes = true;
                    break;
                case "--prune":
                    prune = true;
                    break;
                case "--no-strict":
                    noStrict = true;
                    break;
                case var option when option.StartsWith('-'):
                    throw Args.Unknown(option);
                default:
                    positionals.Add(expanded[i]);
                    break;
            }
        }

        if (positionals.Count == 0)
        {
            throw new CommandLineException("the following arguments are required: FILE");
        }

        var database = DatabaseLoader.LoadFile(positionals[0],
                                               strict: !noStrict,
                                               pruneChoices: prune);
        var items = positionals.Skip(1).ToList();

        if (printBuses)
        {
            ListBuses(database, items, stdout);
        }
        else if (printNodes)
        {
            ListNodes(database, items, stdout);
        }
        else
        {
            ListMessages(database, items, printAll, excludeNormal, excludeExtended,
                         !skipFormatSpecifics, stdout);
        }
    }

    private static void ListBuses(Database database, List<string> items, TextWriter stdout)
    {
        foreach (var bus in database.Buses)
        {
            if (items.Count > 0 && !items.Contains(bus.Name))
            {
                continue;
            }

            stdout.WriteLine($"{bus.Name}:");
            PrintComments(bus.Comments, "  ", stdout);

            if (bus.Baudrate is { } baudrate)
            {
                stdout.WriteLine($"  Baudrate: {baudrate}");
            }

            if (bus.FdBaudrate is { } fdBaudrate)
            {
                stdout.WriteLine("  CAN-FD enabled: True");
                stdout.WriteLine($"  FD Baudrate: {fdBaudrate}");
            }
            else
            {
                stdout.WriteLine("  CAN-FD enabled: False");
            }
        }
    }

    private static void ListNodes(Database database, List<string> items, TextWriter stdout)
    {
        foreach (var node in database.Nodes)
        {
            if (items.Count > 0 && !items.Contains(node.Name))
            {
                continue;
            }

            stdout.WriteLine($"{node.Name}:");
            PrintComments(node.Comments, "  ", stdout);
        }
    }

    private static void ListMessages(
        Database database,
        List<string> items,
        bool printAll,
        bool excludeNormal,
        bool excludeExtended,
        bool printFormatSpecifics,
        TextWriter stdout)
    {
        var messageNames = items;

        if (printAll || items.Count == 0)
        {
            messageNames = database.Messages
                .Where(message => !(message.IsExtendedFrame && excludeExtended))
                .Where(message => !(!message.IsExtendedFrame && excludeNormal))
                .Select(message => message.Name)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (!printAll)
            {
                foreach (var name in messageNames)
                {
                    stdout.WriteLine(name);
                }

                return;
            }
        }

        foreach (var name in messageNames)
        {
            if (!database.TryGetMessageByName(name, out var message))
            {
                stdout.WriteLine($"No message named \"{name}\" has been found in input file.");
                continue;
            }

            PrintMessage(message, printFormatSpecifics, stdout);
        }
    }

    private static void PrintMessage(Message message, bool printFormatSpecifics, TextWriter stdout)
    {
        stdout.WriteLine($"{message.Name}:");
        PrintComments(message.Comments, "  ", stdout);

        if (message.BusName is { } busName)
        {
            stdout.WriteLine($"  Bus: {busName}");
        }

        if (message.Senders.Count > 0)
        {
            stdout.WriteLine($"  Sending ECUs: {string.Join(", ", message.Senders.Order(StringComparer.Ordinal))}");
        }

        stdout.WriteLine($"  Frame ID: 0x{message.FrameId:x} ({message.FrameId})");
        stdout.WriteLine($"  Size: {message.Length} bytes");
        stdout.WriteLine($"  Is extended frame: {message.IsExtendedFrame}");
        stdout.WriteLine($"  Is CAN-FD frame: {message.IsFd}");

        if (message.CycleTime is { } cycleTime)
        {
            stdout.WriteLine($"  Cycle time: {cycleTime} ms");
        }

        // upstream prints AUTOSAR properties here when printFormatSpecifics is set;
        // the model has no AUTOSAR support, so there is nothing to print
        _ = printFormatSpecifics;

        if (message.Signals.Count > 0)
        {
            stdout.WriteLine("  Signal tree:");
            stdout.WriteLine();

            foreach (var line in SignalTreeFormatter.SignalTreeString(message, 1_000_000).Split('\n'))
            {
                stdout.WriteLine($"    {line}");
            }

            stdout.WriteLine();
            stdout.WriteLine("  Signal details:");
        }

        foreach (var signal in message.Signals)
        {
            PrintSignal(message, signal, stdout);
        }
    }

    private static void PrintSignal(Message message, Signal signal, TextWriter stdout)
    {
        var signalType = "Integer";

        if (signal.IsFloat)
        {
            signalType = "Float";
        }
        else if (signal.IsMultiplexer
                 && message.Signals.Any(other => other.MultiplexerSignal == signal.Name))
        {
            signalType = "Multiplex Selector";
        }

        stdout.WriteLine($"    {signal.Name}:");
        PrintComments(signal.Comments, "      ", stdout);

        if (signal.Receivers.Count > 0)
        {
            stdout.WriteLine($"      Receiving ECUs: {string.Join(", ", signal.Receivers.Order(StringComparer.Ordinal))}");
        }

        stdout.WriteLine($"      Internal type: {signalType}");

        if (signal.MultiplexerSignal is { } selectorName)
        {
            stdout.WriteLine($"      Selector signal: {selectorName}");

            var selector = message.GetSignalByName(selectorName);
            var selectorValues = (signal.MultiplexerIds ?? []).Select(id =>
                selector.Choices?.TryGetValue(id, out var choice) == true
                    ? choice.Name
                    : id.ToString());

            stdout.WriteLine($"      Selector values: {string.Join(", ", selectorValues)}");
        }

        stdout.WriteLine($"      Start bit: {signal.StartBit}");
        stdout.WriteLine($"      Length: {signal.Length} bits");
        stdout.WriteLine($"      Byte order: {ByteOrderName(signal.ByteOrder)}");

        var unit = "";

        if (signal.Unit is { Length: > 0 })
        {
            stdout.WriteLine($"      Unit: {signal.Unit}");
            unit = signal.Unit;
        }

        if (signal.Initial is { } initial)
        {
            stdout.WriteLine($"      Initial value: {FormatValue(initial, unit)}");
        }

        if (signal.Invalid is { } invalid)
        {
            stdout.WriteLine($"      Invalid value: {FormatValue(invalid, unit)}");
        }

        stdout.WriteLine($"      Is signed: {signal.IsSigned}");

        if (signal.Minimum is { } minimum)
        {
            stdout.WriteLine($"      Minimum: {FormatNumber(minimum, unit)}");
        }

        if (signal.Maximum is { } maximum)
        {
            stdout.WriteLine($"      Maximum: {FormatNumber(maximum, unit)}");
        }

        var hasOffset = signal.Offset != 0;
        var hasScale = signal.Scale is > 1 + 1e-10 or < 1 - 1e-10;

        if (hasOffset || hasScale)
        {
            stdout.WriteLine($"      Offset: {FormatNumber(signal.Offset, unit)}");
            stdout.WriteLine($"      Scaling factor: {FormatNumber(signal.Scale, unit)}");
        }

        if (signal.Choices is { Count: > 0 } choices)
        {
            stdout.WriteLine("      Named values:");

            foreach (var (value, choice) in choices)
            {
                stdout.WriteLine($"        {value}: {choice.Name}");

                foreach (var (language, comment) in choice.Comments)
                {
                    stdout.WriteLine($"          Comment[{language}]: {comment}");
                }
            }
        }
    }

    private static void PrintComments(Comments? comments, string indent, TextWriter stdout)
    {
        if (comments is null)
        {
            return;
        }

        // upstream's comment dict uses key None for the language-less entry
        if (comments.Default is { } defaultComment)
        {
            stdout.WriteLine($"{indent}Comment[None]: {defaultComment}");
        }

        foreach (var (language, comment) in comments.ByLanguage)
        {
            stdout.WriteLine($"{indent}Comment[{language}]: {comment}");
        }
    }

    private static string FormatValue(SignalValue value, string unit)
    {
        var text = MessageFormatter.FormatValue(value);

        return unit.Length == 0 || value.IsLabel ? text : $"{text} {unit}";
    }

    private static string FormatNumber(double value, string unit)
    {
        var text = PythonFormat.Number(value);

        return unit.Length == 0 ? text : $"{text} {unit}";
    }

    internal static string ByteOrderName(ByteOrder byteOrder) =>
        byteOrder == ByteOrder.BigEndian ? "big_endian" : "little_endian";
}
