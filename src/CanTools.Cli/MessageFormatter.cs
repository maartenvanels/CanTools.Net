using CanTools.Model;

namespace CanTools.Cli;

/// <summary>
/// Renders decoded messages for the decode subcommand, matching the output of
/// cantools' subparsers/__utils__.py. Container messages are not ported.
/// </summary>
internal static class MessageFormatter
{
    public static string FormatByFrameId(
        Database database,
        uint frameId,
        byte[] data,
        bool decodeChoices,
        bool singleLine,
        bool allowTruncated,
        bool allowExcess)
    {
        if (!database.TryGetMessageByFrameId(frameId, out var message))
        {
            return $" Unknown frame id {frameId} (0x{frameId:x})";
        }

        try
        {
            var decoded = message.Decode(data, decodeChoices,
                                         allowTruncated: allowTruncated,
                                         allowExcess: allowExcess);

            return Format(message, decoded, singleLine);
        }
        catch (DecodeException exception)
        {
            return $" {exception.Message}";
        }
    }

    public static string Format(
        Message message, IReadOnlyDictionary<string, SignalValue> decoded, bool singleLine)
    {
        var signals = FormatSignals(message, decoded);

        return singleLine
            ? $" {message.Name}({string.Join(", ", signals)})"
            : $"\n{message.Name}(\n{string.Join(",\n", signals.Select(s => "    " + s))}\n)";
    }

    public static string FormatValue(SignalValue value) =>
        value.IsLabel ? value.Label!
        : value.IsInteger ? value.ToString()
        : PythonFormat.Repr(value.ToDouble());

    private static List<string> FormatSignals(
        Message message, IReadOnlyDictionary<string, SignalValue> decoded)
    {
        var lines = new List<string>();

        foreach (var signal in message.Signals)
        {
            if (!decoded.TryGetValue(signal.Name, out var value))
            {
                continue;
            }

            var text = FormatValue(value);

            lines.Add(signal.Unit is null || value.IsLabel
                ? $"{signal.Name}: {text}"
                : $"{signal.Name}: {text} {signal.Unit}");
        }

        return lines;
    }
}
