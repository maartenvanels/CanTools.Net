using System.Text;
using System.Text.RegularExpressions;

namespace CanTools.Formats.Eds;

/// <summary>
/// A fidelity-preserving, editable INI model: it keeps every line of the source
/// file — comments, blank lines, section and key order — and mutates only the lines
/// it is told to. <see cref="ToString"/> reproduces an unchanged document verbatim.
/// This is the write-side counterpart to <see cref="IniFile"/> (the fast read-only
/// parser used by <see cref="EdsReader"/>), kept separate so the reader is untouched.
/// </summary>
internal sealed partial class IniDocument
{
    private readonly List<string> _preamble;
    private readonly List<Section> _sections;
    private readonly string _newline;
    private readonly bool _trailingNewline;

    private IniDocument(
        List<string> preamble, List<Section> sections, string newline, bool trailingNewline)
    {
        _preamble = preamble;
        _sections = sections;
        _newline = newline;
        _trailingNewline = trailingNewline;
    }

    [GeneratedRegex(@"^([0-9A-Fa-f]{4})(?:[Ss]ub([0-9A-Fa-f]+))?$")]
    private static partial Regex ObjectHeader();

    public static IniDocument Parse(string text)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var trailingNewline = text.EndsWith('\n');

        var raw = text.Split('\n').Select(line => line.TrimEnd('\r')).ToList();

        // A trailing newline leaves an empty final element that is not a real line.
        if (trailingNewline && raw.Count > 0 && raw[^1].Length == 0)
        {
            raw.RemoveAt(raw.Count - 1);
        }

        var preamble = new List<string>();
        var sections = new List<Section>();
        Section? current = null;

        foreach (var line in raw)
        {
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('[') && trimmed.Contains(']'))
            {
                var name = trimmed[1..trimmed.IndexOf(']')];
                current = new Section(line, name);
                sections.Add(current);
                continue;
            }

            (current?.Lines ?? preamble).Add(line);
        }

        return new IniDocument(preamble, sections, newline, trailingNewline);
    }

    public IniDocument Clone() => Parse(ToString());

    /// <summary>
    /// Sets <paramref name="key"/> to <paramref name="value"/> in the section named
    /// <paramref name="sectionName"/>, creating the section (after [DeviceInfo] if
    /// present, otherwise at the end) when it does not exist.
    /// </summary>
    public void UpsertInSection(string sectionName, string key, string value)
    {
        var section = _sections.Find(s => s.Name == sectionName);

        if (section is null)
        {
            section = new Section($"[{sectionName}]", sectionName);
            var after = _sections.FindIndex(s => s.Name == "DeviceInfo");

            if (after >= 0)
            {
                _sections[after].Lines.Add("");   // a blank line separates the new section
                _sections.Insert(after + 1, section);
            }
            else
            {
                _sections.Add(section);
            }
        }

        Upsert(section.Lines, key, value);
    }

    /// <summary>
    /// Sets <paramref name="key"/> in the object section for
    /// <paramref name="index"/>/<paramref name="subindex"/> (a subindex of null means
    /// the plain <c>[XXXX]</c> section). Returns false when there is no such section.
    /// </summary>
    public bool TryUpsertObject(int index, int? subindex, string key, string value)
    {
        var section = _sections.Find(s => Matches(s.Name, index, subindex));

        if (section is null)
        {
            return false;
        }

        Upsert(section.Lines, key, value);
        return true;
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        var lines = _preamble.Concat(_sections.SelectMany(s => s.Lines.Prepend(s.Header)));
        builder.AppendJoin(_newline, lines);

        if (_trailingNewline)
        {
            builder.Append(_newline);
        }

        return builder.ToString();
    }

    private static bool Matches(string sectionName, int index, int? subindex)
    {
        if (ObjectHeader().Match(sectionName) is not { Success: true } match)
        {
            return false;
        }

        if (Convert.ToInt32(match.Groups[1].Value, 16) != index)
        {
            return false;
        }

        var hasSub = match.Groups[2].Success;

        return subindex is { } wanted
            ? hasSub && Convert.ToInt32(match.Groups[2].Value, 16) == wanted
            : !hasSub;
    }

    // Replaces the line that defines key, or inserts a new one before any trailing
    // blank lines so the section keeps its separation from the next.
    private static void Upsert(List<string> lines, string key, string value)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (IsKeyLine(lines[i], key))
            {
                lines[i] = $"{key}={value}";
                return;
            }
        }

        var insertAt = lines.Count;

        while (insertAt > 0 && lines[insertAt - 1].Trim().Length == 0)
        {
            insertAt--;
        }

        lines.Insert(insertAt, $"{key}={value}");
    }

    private static bool IsKeyLine(string raw, string key)
    {
        var line = raw.Trim();

        if (line.Length == 0 || line[0] is ';' or '#')
        {
            return false;
        }

        var delimiter = line.IndexOfAny(['=', ':']);

        return delimiter >= 0 && line[..delimiter].TrimEnd() == key;
    }

    private sealed class Section
    {
        public Section(string header, string name)
        {
            Header = header;
            Name = name;
        }

        public string Header { get; }

        public string Name { get; }

        public List<string> Lines { get; } = [];
    }
}
