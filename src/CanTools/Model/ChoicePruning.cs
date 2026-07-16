using System.Text.RegularExpressions;

namespace CanTools.Model;

/// <summary>
/// Shortens choice names by stripping redundant prefixes, the port of upstream's
/// prune_signal_choices: for multiple choices, the longest common prefix ending in
/// an underscore is removed as long as the remainders stay valid identifiers; a
/// single choice keeps only its last letters-and-underscores segment.
/// </summary>
internal static partial class ChoicePruning
{
    [GeneratedRegex("^[0-9A-Za-z_]*?_([A-Za-z_]+)$")]
    private static partial Regex SingleChoiceSuffix();

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex ValidIdentifier();

    public static void PruneDatabaseChoices(Database database)
    {
        foreach (var message in database.Messages)
        {
            foreach (var signal in message.Signals)
            {
                PruneSignalChoices(signal);
            }
        }
    }

    public static void PruneSignalChoices(Signal signal)
    {
        var choices = signal.Choices;

        if (choices is null)
        {
            return;
        }

        if (choices.Count == 1)
        {
            var (value, choice) = choices.Single();
            var match = SingleChoiceSuffix().Match(choice.Name);

            if (match.Success)
            {
                ReplaceChoices(signal, new Dictionary<long, NamedSignalValue>
                {
                    [value] = new(value, match.Groups[1].Value, choice.Comments),
                });
            }

            return;
        }

        var names = choices.Values.Select(choice => choice.Name).ToList();
        var fullPrefix = CommonPrefix(names);
        var underscore = fullPrefix.LastIndexOf('_');

        if (underscore < 0)
        {
            return;
        }

        fullPrefix = fullPrefix[..underscore];

        if (fullPrefix.Length == 0)
        {
            return;
        }

        var segments = fullPrefix.Split('_');

        // Find the longest prefix whose removal keeps every name a valid identifier.
        for (var count = segments.Length; count >= 1; count--)
        {
            var prefix = string.Join('_', segments[..count]) + "_";

            if (names.All(name => ValidIdentifier().IsMatch(name[prefix.Length..])))
            {
                ReplaceChoices(signal, choices.ToDictionary(
                    entry => entry.Key,
                    entry => new NamedSignalValue(
                        entry.Value.Value,
                        entry.Value.Name[prefix.Length..],
                        entry.Value.Comments)));
                return;
            }
        }
    }

    private static void ReplaceChoices(
        Signal signal, IReadOnlyDictionary<long, NamedSignalValue> choices)
    {
        signal.Conversion = Conversion.Create(
            signal.Scale, signal.Offset, choices, signal.IsFloat);
    }

    private static string CommonPrefix(IReadOnlyList<string> values)
    {
        var prefix = values[0];

        foreach (var value in values)
        {
            var length = 0;

            while (length < prefix.Length && length < value.Length && prefix[length] == value[length])
            {
                length++;
            }

            prefix = prefix[..length];
        }

        return prefix;
    }
}
