namespace CanTools.Model;

/// <summary>Reorders the signals of a message, e.g. for display or when writing files.</summary>
public delegate IReadOnlyList<Signal> SignalSort(IReadOnlyList<Signal> signals);

/// <summary>Standard signal orderings.</summary>
public static class SignalSorts
{
    /// <summary>Sort by position in the frame (the default for messages).</summary>
    public static readonly SignalSort ByStartBit =
        signals => signals.OrderBy(NetworkStartBit).ToList();

    /// <summary>Sort by position in the frame, last signal first.</summary>
    public static readonly SignalSort ByStartBitReversed =
        signals => signals.OrderBy(NetworkStartBit).Reverse().ToList();

    /// <summary>Sort alphabetically by signal name.</summary>
    public static readonly SignalSort ByName =
        signals => signals.OrderBy(signal => signal.Name, StringComparer.Ordinal).ToList();

    /// <summary>Keep the declaration order.</summary>
    public static readonly SignalSort None = signals => signals;

    internal static int NetworkStartBit(Signal signal) =>
        signal.ByteOrder == ByteOrder.BigEndian
            ? 8 * (signal.StartBit / 8) + (7 - signal.StartBit % 8)
            : signal.StartBit;
}
