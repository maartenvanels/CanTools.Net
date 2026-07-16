using System.Globalization;
using System.Text.RegularExpressions;

namespace CanTools.Logs;

/// <summary>
/// Parses CAN log files line by line, auto-detecting the format from the first
/// parseable line: candump (plain, -tz, -ta, -tA, -l) and PCAN trace 1.0–2.1.
/// </summary>
public sealed partial class LogParser
{
    private readonly TextReader? _reader;
    private LogPattern? _pattern;

    public LogParser(TextReader? reader = null)
    {
        _reader = reader;
    }

    /// <summary>Parses one log line, or returns null when it is not a CAN frame.</summary>
    public LogEntry? Parse(string line)
    {
        _pattern ??= Patterns.FirstOrDefault(pattern => pattern.Regex.IsMatch(line));

        if (_pattern is null)
        {
            return null;
        }

        var match = _pattern.Regex.Match(line);

        return match.Success ? _pattern.Unpack(match) : null;
    }

    /// <summary>
    /// Reads all lines from the reader, yielding the raw line and its parsed frame.
    /// Unparseable lines are skipped unless <paramref name="keepUnknowns"/> is set.
    /// </summary>
    public IEnumerable<(string Line, LogEntry? Entry)> ReadLines(bool keepUnknowns = false)
    {
        if (_reader is null)
        {
            yield break;
        }

        while (_reader.ReadLine() is { } line)
        {
            var entry = Parse(line);

            if (entry is not null || keepUnknowns)
            {
                yield return (line, entry);
            }
        }
    }

    /// <summary>Reads all parseable frames from the reader.</summary>
    public IEnumerable<LogEntry> ReadEntries()
    {
        foreach (var (_, entry) in ReadLines())
        {
            yield return entry!;
        }
    }

    private sealed record LogPattern(Regex Regex, Func<Match, LogEntry?> Unpack);

    private static readonly LogPattern[] Patterns =
    [
        new(CandumpDefault(), match => UnpackCandump(match, (_, _) => (TimestampFormat.Missing, null, null))),
        new(CandumpTimestamped(), match => UnpackCandump(match, RelativeOrUnixTimestamp)),
        new(CandumpDefaultLog(), match => UnpackCandump(match, UnixTimestamp)),
        new(CandumpAbsoluteLog(), match => UnpackCandump(match, WallClockTimestamp)),
        new(PcanV21(), match => UnpackPcan(match, hasType: true, hasChannel: true)),
        new(PcanV20(), match => UnpackPcan(match, hasType: true, hasChannel: false)),
        new(PcanV13(), match => UnpackPcan(match, hasType: true, hasChannel: true)),
        new(PcanV12(), match => UnpackPcan(match, hasType: true, hasChannel: true)),
        new(PcanV11(), match => UnpackPcan(match, hasType: true, hasChannel: false)),
        new(PcanV10(), match => UnpackPcan(match, hasType: false, hasChannel: false)),
    ];

    // candump vcan0:                vcan0  1F0   [8]  00 00 00 00 00 00 1B C1
    [GeneratedRegex(@"^\s*?(?<channel>\S+)\s+(?<can_id>[0-9A-F]+)\s+\[\d+\]\s*(?<can_data>remote request|[0-9A-F ]*).*?$")]
    private static partial Regex CandumpDefault();

    // candump vcan0 -tz:            (000.000000)  vcan0  0C8   [8]  F0 00 ...
    [GeneratedRegex(@"^\s*?\((?<timestamp>[\d.]+)\)\s+(?<channel>\S+)\s+(?<can_id>[0-9A-F]+)\s+\[\d+\]\s*(?<can_data>remote request|[0-9A-F ]*).*?$")]
    private static partial Regex CandumpTimestamped();

    // candump -l:                   (1579857014.345944) can2 486#82967A6B006B07F8
    [GeneratedRegex(@"^\s*?\((?<timestamp>[\d.]+?)\)\s+?(?<channel>\S+)\s+?(?<can_id>[0-9A-F]+?)#(#[0-9A-F])?(?<can_data>R|([0-9A-Fa-f]{2})*)(\s+[RT])?$")]
    private static partial Regex CandumpDefaultLog();

    // candump vcan0 -tA:            (2020-12-19 12:04:45.485261)  vcan0  0C8   [8]  F0 ...
    [GeneratedRegex(@"^\s*?\((?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d+)\)\s+(?<channel>\S+)\s+(?<can_id>[0-9A-F]+)\s+\[\d+\]\s*(?<can_data>remote request|[0-9A-F ]*).*?$")]
    private static partial Regex CandumpAbsoluteLog();

    [GeneratedRegex(@"^\s*?\d+\)\s*?(?<timestamp>\d+)\s+(?<can_id>[0-9A-F]+)\s+(?<dlc>[0-9])\s+(?<can_data>RTR|[0-9A-F ]*)$")]
    private static partial Regex PcanV10();

    [GeneratedRegex(@"^\s*?\d+\)\s*?(?<timestamp>\d+.\d+)\s+(?<type>\w+)\s+(?<can_id>[0-9A-F]+)\s+(?<dlc>[0-9])\s+(?<can_data>RTR|[0-9A-F ]*)$")]
    private static partial Regex PcanV11();

    [GeneratedRegex(@"^\s*?\d+\)\s*?(?<timestamp>\d+.\d+)\s+(?<channel>[0-9])\s+(?<type>\w+)\s+(?<can_id>[0-9A-F]+)\s+(?<dlc>[0-9])\s+(?<can_data>RTR|[0-9A-F ]*)$")]
    private static partial Regex PcanV12();

    [GeneratedRegex(@"^\s*?\d+\)\s*?(?<timestamp>\d+.\d+)\s+(?<channel>[0-9])\s+(?<type>\w+)\s+(?<can_id>[0-9A-F]+)\s+-\s+(?<dlc>[0-9])\s+(?<can_data>RTR|[0-9A-F ]*)$")]
    private static partial Regex PcanV13();

    [GeneratedRegex(@"^\s*?\d+?\s*?(?<timestamp>\d+.\d+)\s+(?<type>\w+)\s+(?<can_id>[0-9A-F]+)\s+(?<rxtx>\w+)\s+(?<dlc>[0-9]+)(\s+(?<can_data>[0-9A-F ]*))?$")]
    private static partial Regex PcanV20();

    [GeneratedRegex(@"^\s*?\d+?\s*?(?<timestamp>\d+.\d+)\s+(?<type>\w+)\s+(?<channel>[0-9])\s+(?<can_id>[0-9A-F]+)\s+(?<rxtx>.+)\s+-\s+(?<dlc>[0-9]+)(\s+(?<can_data>[0-9A-F ]*))?$")]
    private static partial Regex PcanV21();

    private static LogEntry UnpackCandump(
        Match match,
        Func<Match, string, (TimestampFormat, DateTime?, TimeSpan?)> parseTimestamp)
    {
        var idText = match.Groups["can_id"].Value;
        var dataText = match.Groups["can_data"].Value;
        var isRemoteFrame = dataText is "remote request" or "R";
        var data = isRemoteFrame ? [] : Convert.FromHexString(dataText.Replace(" ", ""));
        var (format, timestamp, offset) = parseTimestamp(match, match.Groups["timestamp"].Value);

        return new LogEntry(
            channel: match.Groups["channel"].Value,
            frameId: uint.Parse(idText, NumberStyles.HexNumber),
            isExtendedFrame: idText.Length > 3,
            data: data,
            isRemoteFrame: isRemoteFrame,
            timestampFormat: format,
            timestamp: timestamp,
            timeOffset: offset);
    }

    // Timestamps below 1991-01-01 are taken to be relative: CAN did not ship in a
    // production car before the Mercedes-Benz W140.
    private static (TimestampFormat, DateTime?, TimeSpan?) RelativeOrUnixTimestamp(
        Match match, string text)
    {
        var ticks = TicksFromSeconds(text);

        if (ticks < 662688000L * TimeSpan.TicksPerSecond)
        {
            return (TimestampFormat.Relative, null, TimeSpan.FromTicks(ticks));
        }

        return UnixTimestamp(match, text);
    }

    private static (TimestampFormat, DateTime?, TimeSpan?) UnixTimestamp(Match match, string text)
    {
        var timestamp = DateTimeOffset.UnixEpoch.AddTicks(TicksFromSeconds(text));

        return (TimestampFormat.Absolute, timestamp.ToLocalTime().DateTime, null);
    }

    private static (TimestampFormat, DateTime?, TimeSpan?) WallClockTimestamp(Match match, string text)
    {
        var timestamp = DateTime.ParseExact(
            text, "yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);

        return (TimestampFormat.Absolute, timestamp, null);
    }

    private static LogEntry? UnpackPcan(Match match, bool hasType, bool hasChannel)
    {
        var type = match.Groups["type"].Value;

        if (hasType && type is "Error" or "Warng")
        {
            return null;
        }

        var idText = match.Groups["can_id"].Value;
        var dataText = match.Groups["can_data"].Value;
        var isRemoteFrame = dataText == "RTR" || type == "RR";
        var data = isRemoteFrame ? [] : Convert.FromHexString(dataText.Replace(" ", ""));

        return new LogEntry(
            channel: hasChannel ? "pcan" + match.Groups["channel"].Value : "pcanx",
            frameId: uint.Parse(idText, NumberStyles.HexNumber),
            isExtendedFrame: idText.Length > 4,
            data: data,
            isRemoteFrame: isRemoteFrame,
            timestampFormat: TimestampFormat.Relative,
            timeOffset: TimeSpan.FromTicks(TicksFromSeconds(match.Groups["timestamp"].Value) / 1000));
    }

    // Parses "12.345678" into ticks without going through floating point, so
    // microsecond timestamps stay exact.
    private static long TicksFromSeconds(string text)
    {
        var parts = text.Split('.');
        var ticks = long.Parse(parts[0], CultureInfo.InvariantCulture) * TimeSpan.TicksPerSecond;

        if (parts.Length > 1 && parts[1].Length > 0)
        {
            var fraction = parts[1].Length > 7 ? parts[1][..7] : parts[1];
            ticks += long.Parse(fraction, CultureInfo.InvariantCulture)
                     * (long)Math.Pow(10, 7 - fraction.Length);
        }

        return ticks;
    }
}
