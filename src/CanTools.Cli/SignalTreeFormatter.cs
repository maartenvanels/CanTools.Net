using System.Text;
using CanTools.Model;

namespace CanTools.Cli;

/// <summary>
/// Renders a message's multiplexing hierarchy as the ASCII tree of cantools'
/// dump/formatting.py signal_tree_string, used by the list and dump subcommands.
/// </summary>
internal static class SignalTreeFormatter
{
    private const string CommentColor = "\x1b[94m";
    private const string ResetColor = "\x1b[0m";

    public static string SignalTreeString(
        Message message, int consoleWidth = 80, bool withComments = false)
    {
        var lines = FormatLevel(message, message.SignalTree, consoleWidth, withComments);

        return string.Join("\n", lines.Select(line => "   " + line).Prepend("-- {root}"));
    }

    private static List<string> FormatLevel(
        Message message, IReadOnlyList<SignalTreeNode> nodes, int width, bool withComments)
    {
        var lines = new List<string>();

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            lines.Add(FormatSignalLine(message, node.Name, width, withComments));

            if (node.Multiplexed is not null)
            {
                var prefix = index < nodes.Count - 1 ? "|   " : "    ";
                lines.AddRange(FormatMux(message, node, width, withComments)
                    .Select(line => prefix + line));
            }
        }

        return lines;
    }

    private static List<string> FormatMux(
        Message message, SignalTreeNode selector, int width, bool withComments)
    {
        var selectorSignal = message.GetSignalByName(selector.Name);
        var multiplexed = selector.Multiplexed!.OrderBy(entry => entry.Key).ToList();
        var lines = new List<string>();

        for (var index = 0; index < multiplexed.Count; index++)
        {
            var (multiplexerId, nodes) = multiplexed[index];
            var description = selectorSignal.Choices?.TryGetValue(multiplexerId, out var choice) == true
                ? $"{choice.Name} ({multiplexerId})"
                : multiplexerId.ToString();

            lines.Add($"+-- {description}");

            var prefix = index < multiplexed.Count - 1 ? "|   " : "    ";
            lines.AddRange(FormatLevel(message, nodes, width, withComments)
                .Select(line => prefix + line));
        }

        return lines;
    }

    private static string FormatSignalLine(
        Message message, string signalName, int width, bool withComments)
    {
        var line = signalName;

        if (withComments)
        {
            var signal = message.GetSignalByName(signalName);
            var parts = new List<string>();

            if (signal.Comment is { } comment)
            {
                parts.Add(comment);
            }

            if (signal.Unit is { } unit && unit.Length > 0)
            {
                parts.Add($"[{unit}]");
            }

            if (parts.Count > 0)
            {
                line = $"{signalName} {CommentColor}{string.Join(" ", parts)}{ResetColor}";
            }
        }

        return Wrap(line, width - 2, "+-- ", new string(' ', 8 + signalName.Length));
    }

    /// <summary>Greedy word wrap like Python's textwrap.wrap, joined with newlines.</summary>
    private static string Wrap(string text, int width, string initialIndent, string subsequentIndent)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = new StringBuilder(initialIndent);
        var currentIsEmpty = true;

        foreach (var word in words)
        {
            var separatorLength = currentIsEmpty ? 0 : 1;

            if (!currentIsEmpty && current.Length + separatorLength + word.Length > width)
            {
                lines.Add(current.ToString());
                current = new StringBuilder(subsequentIndent);
                currentIsEmpty = true;
            }

            current.Append(currentIsEmpty ? "" : " ").Append(word);
            currentIsEmpty = false;
        }

        lines.Add(current.ToString());

        return string.Join("\n", lines);
    }
}
