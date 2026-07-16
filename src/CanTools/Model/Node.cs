namespace CanTools.Model;

/// <summary>A node (ECU) on the CAN bus.</summary>
public sealed class Node
{
    public Node(string name, Comments? comment = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Comments = comment;
    }

    public string Name { get; }

    /// <summary>The node's comment, or null if unavailable.</summary>
    public string? Comment => Comments?.Resolve();

    public Comments? Comments { get; }

    /// <summary>DBC-specific properties such as attributes, or null.</summary>
    public DbcSpecifics? Dbc { get; init; }

    public override string ToString() => $"Node {Name}";
}
