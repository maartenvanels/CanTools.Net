namespace CanTools.Formats.Eds;

internal sealed class IniSection
{
    public IniSection(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>Options in file order; keys are case-sensitive.</summary>
    public Dictionary<string, string> Options { get; } = [];

    public string? GetValueOrNull(string key) =>
        Options.TryGetValue(key, out var value) ? value : null;
}

/// <summary>
/// Minimal INI reader matching Python's RawConfigParser as python-canopen uses it:
/// case-sensitive sections and keys, '=' or ':' delimiters, '#'/';' full-line
/// comments, ';' inline comments only after whitespace, '%' has no special meaning,
/// and empty values are allowed.
/// </summary>
internal sealed class IniFile
{
    private readonly Dictionary<string, IniSection> _byName = [];

    private IniFile(List<IniSection> sections)
    {
        Sections = sections;

        foreach (var section in sections)
        {
            if (!_byName.TryAdd(section.Name, section))
            {
                throw new ParseException($"Duplicate section [{section.Name}].");
            }
        }
    }

    /// <summary>The sections in file order.</summary>
    public IReadOnlyList<IniSection> Sections { get; }

    public IniSection? Find(string name) => _byName.GetValueOrDefault(name);

    public static IniFile Parse(string text)
    {
        var sections = new List<IniSection>();
        IniSection? current = null;
        var lineNumber = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r').Trim();

            if (line.Length == 0 || line[0] is ';' or '#')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var end = line.IndexOf(']');

                if (end < 0)
                {
                    throw new ParseException($"Malformed section header on line {lineNumber}.");
                }

                current = new IniSection(line[1..end]);
                sections.Add(current);
                continue;
            }

            var delimiter = line.IndexOfAny(['=', ':']);

            if (delimiter < 0 || current is null)
            {
                throw new ParseException($"Unrecognized line {lineNumber}: '{line}'.");
            }

            var key = line[..delimiter].TrimEnd();
            var value = StripInlineComment(line[(delimiter + 1)..].Trim());

            if (!current.Options.TryAdd(key, value))
            {
                throw new ParseException(
                    $"Duplicate key '{key}' in section [{current.Name}].");
            }
        }

        return new IniFile(sections);
    }

    // An inline comment starts at a ';' preceded by whitespace.
    private static string StripInlineComment(string value)
    {
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] == ';' && char.IsWhiteSpace(value[i - 1]))
            {
                return value[..i].TrimEnd();
            }
        }

        return value;
    }
}
