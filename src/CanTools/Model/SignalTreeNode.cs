namespace CanTools.Model;

/// <summary>
/// One entry of a message's signal tree. A plain signal is just a name; a multiplexer
/// selector additionally carries the signal names available per multiplexer value.
/// </summary>
public sealed class SignalTreeNode
{
    internal SignalTreeNode(string name,
                            IReadOnlyDictionary<long, IReadOnlyList<SignalTreeNode>>? multiplexed = null)
    {
        Name = name;
        Multiplexed = multiplexed;
    }

    public string Name { get; }

    /// <summary>
    /// The signal tree per multiplexer value when this signal is a multiplexer
    /// selector, otherwise null.
    /// </summary>
    public IReadOnlyDictionary<long, IReadOnlyList<SignalTreeNode>>? Multiplexed { get; }

    public override string ToString() => Multiplexed is null
        ? Name
        : $"{Name}: {{{string.Join(", ", Multiplexed.Keys)}}}";
}
