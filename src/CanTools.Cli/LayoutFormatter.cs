using System.Text;
using CanTools.Model;

namespace CanTools.Cli;

/// <summary>
/// Renders a message layout as the ASCII art of cantools' dump/formatting.py
/// layout_string: one arrow per signal from LSB "x" to MSB "&lt;", overlaps as "X".
/// </summary>
internal static class LayoutFormatter
{
    private const string Separator = "+---+---+---+---+---+---+---+---+";

    public static string LayoutString(Message message)
    {
        var (lines, numberOfBytes, numberWidth) = FormatByteLines(message);
        lines = AddHorizontalLines(lines, numberWidth);
        lines = AddSignalNames(message, lines, numberOfBytes, numberWidth);
        lines = AddHeaderLines(lines, numberWidth);
        lines = AddYAxisName(lines);

        return string.Join("\n", lines.Select(line => line.TrimEnd()));
    }

    /// <summary>The leftmost bit of the signal in the layout, like utils.start_bit.</summary>
    private static int StartBit(Signal signal) =>
        signal.ByteOrder == ByteOrder.BigEndian
            ? 8 * (signal.StartBit / 8) + (7 - signal.StartBit % 8)
            : signal.StartBit;

    private static IEnumerable<string> FormatBig(Message message) =>
        message.Signals
            .Where(signal => signal.ByteOrder == ByteOrder.BigEndian)
            .Select(signal => new string(' ', 3 * StartBit(signal))
                              + "<" + new string('-', 3 * signal.Length - 2) + "x");

    private static IEnumerable<string> FormatLittle(Message message)
    {
        foreach (var signal in message.Signals)
        {
            if (signal.ByteOrder == ByteOrder.BigEndian)
            {
                continue;
            }

            var formatted = new string(' ', 3 * signal.StartBit)
                            + "x" + new string('-', 3 * signal.Length - 2) + "<";
            var end = signal.StartBit + signal.Length;

            if (end % 8 != 0)
            {
                formatted += new string(' ', 3 * (8 - end % 8));
            }

            // little-endian arrows run right to left, so mirror every byte-triple row
            yield return string.Concat(
                Enumerable.Range(0, formatted.Length / 24)
                    .Select(i => new string(formatted.Skip(i * 24).Take(24).Reverse().ToArray())));
        }
    }

    private static (List<string> Lines, int NumberOfBytes, int NumberWidth) FormatByteLines(
        Message message)
    {
        var signals = FormatBig(message).Concat(FormatLittle(message)).ToList();

        if (signals.Count > 0)
        {
            var length = signals.Max(signal => signal.Length);

            if (length % 24 != 0)
            {
                length += 24 - length % 24;
            }

            signals = signals.Select(signal => signal.PadRight(length)).ToList();
        }

        var union = new StringBuilder(signals.Count > 0 ? signals[0].Length : 0);

        for (var i = 0; i < (signals.Count > 0 ? signals[0].Length : 0); i++)
        {
            int head = 0, dash = 0, tail = 0;

            foreach (var signal in signals)
            {
                switch (signal[i])
                {
                    case '<': head++; break;
                    case '-': dash++; break;
                    case 'x': tail++; break;
                }
            }

            union.Append(head + dash + tail > 1 ? 'X'
                : head == 1 ? '<'
                : dash == 1 ? '-'
                : tail == 1 ? 'x'
                : ' ');
        }

        var byteLines = new List<string>();

        for (var i = 0; i < union.Length; i += 24)
        {
            byteLines.Add(union.ToString(i, 24));
        }

        while (byteLines.Count < message.Length)
        {
            byteLines.Add(new string(' ', 24));
        }

        var lines = new List<string>();

        foreach (var byteLine in byteLines)
        {
            var line = new StringBuilder();
            var previous = '\0';

            for (var i = 0; i < 24; i += 3)
            {
                var triple = byteLine.Substring(i, 3);

                if (i == 0 || " <>x".Contains(triple[0]))
                {
                    line.Append('|');
                }
                else if (triple[0] == 'X')
                {
                    line.Append(previous is 'X' or '-' ? previous : '|');
                }
                else
                {
                    line.Append('-');
                }

                line.Append(triple);
                previous = triple[2];
            }

            line.Append('|');
            lines.Add(line.ToString());
        }

        var numberWidth = lines.Count.ToString().Length + 4;
        var numbered = lines
            .Select((line, number) => number.ToString().PadLeft(numberWidth - 1) + " " + line)
            .ToList();

        return (numbered, lines.Count, numberWidth);
    }

    private static List<string> AddHorizontalLines(List<string> byteLines, int numberWidth)
    {
        var padding = new string(' ', numberWidth);
        var lines = new List<string>();

        foreach (var byteLine in byteLines)
        {
            lines.Add(byteLine);
            lines.Add(padding + Separator);
        }

        return lines;
    }

    private static List<string> AddSignalNames(
        Message message, List<string> inputLines, int numberOfBytes, int numberWidth)
    {
        var padding = new string(' ', numberWidth);
        var signalsPerByte = Enumerable.Range(0, numberOfBytes)
            .Select(_ => new List<(int Bit, string Text)>())
            .ToList();

        foreach (var signal in message.Signals)
        {
            var offset = StartBit(signal) + signal.Length - 1;
            var nameBit = signal.ByteOrder == ByteOrder.BigEndian
                ? 8 * (offset / 8) + (7 - offset % 8)
                : offset;

            signalsPerByte[nameBit / 8].Add((nameBit % 8, "+-- " + signal.Name));
        }

        var signalLinesPerByte = new List<List<string>>();

        foreach (var byteSignals in signalsPerByte)
        {
            var sorted = byteSignals
                .OrderBy(signal => signal.Bit)
                .ThenBy(signal => signal.Text, StringComparer.Ordinal)
                .ToList();
            var signalLines = new List<string>();

            foreach (var signal in sorted)
            {
                var chars = (new string(' ', 4 * (7 - signal.Bit)) + padding + "  " + signal.Text)
                    .ToCharArray();

                foreach (var other in sorted)
                {
                    if (other.Bit > signal.Bit)
                    {
                        chars[numberWidth + 2 + 4 * (7 - other.Bit)] = '|';
                    }
                }

                signalLines.Add(new string(chars));
            }

            signalLinesPerByte.Add(signalLines);
        }

        var lines = new List<string>();

        for (var number = 0; number < numberOfBytes; number++)
        {
            lines.AddRange(inputLines.Skip(2 * number).Take(2));

            if (signalLinesPerByte[number].Count > 0)
            {
                lines.AddRange(signalLinesPerByte[number]);

                if (number + 1 < numberOfBytes)
                {
                    lines.Add(padding + Separator);
                }
            }
        }

        return lines;
    }

    private static List<string> AddHeaderLines(List<string> lines, int numberWidth)
    {
        var padding = new string(' ', numberWidth);

        return new List<string>
        {
            padding + "               Bit",
            padding,
            padding + "  7   6   5   4   3   2   1   0",
            padding + Separator,
        }.Concat(lines).ToList();
    }

    private static List<string> AddYAxisName(List<string> lines)
    {
        var matrixLines = lines.Count - 3;

        if (matrixLines < 5)
        {
            lines = lines.Concat(Enumerable.Repeat("     ", 5 - matrixLines)).ToList();
        }

        var startIndex = Math.Max(4 + ((matrixLines - 4) / 2 - 1), 4);

        return lines
            .Select((line, index) => index >= startIndex && index < startIndex + 4
                ? " " + "Byte"[index - startIndex] + line
                : "  " + line)
            .ToList();
    }
}
