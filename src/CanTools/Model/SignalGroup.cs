namespace CanTools.Model;

/// <summary>
/// A group of signals within a message, e.g. signals that have to be updated together.
/// </summary>
public sealed class SignalGroup
{
    public SignalGroup(string name, int repetitions = 1, IReadOnlyList<string>? signalNames = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Repetitions = repetitions;
        SignalNames = signalNames ?? [];
    }

    public string Name { get; }

    public int Repetitions { get; }

    public IReadOnlyList<string> SignalNames { get; }

    public override string ToString() => $"SignalGroup {Name} ({SignalNames.Count} signals)";
}
