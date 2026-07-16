namespace CanTools.Formats.Dbc;

/// <summary>
/// Maps names to unique DBC-compatible short names: names are truncated to 32
/// characters; colliding names get a numeric suffix on their first 27 characters.
/// </summary>
internal sealed class LongNamesConverter
{
    private readonly Dictionary<string, string> _longToShort = [];
    private readonly HashSet<string> _taken = [];

    public LongNamesConverter(IEnumerable<string> names)
    {
        foreach (var name in names.Distinct().OrderBy(n => n.Length).ThenBy(n => n, StringComparer.Ordinal))
        {
            var shortName = name[..Math.Min(32, name.Length)];
            var index = 0;

            while (_taken.Contains(shortName))
            {
                shortName = shortName[..Math.Min(27, shortName.Length)] + $"_{index:d4}";
                index++;
            }

            _longToShort[name] = shortName;
            _taken.Add(shortName);
        }
    }

    public string Shorten(string name) => _longToShort[name];
}
