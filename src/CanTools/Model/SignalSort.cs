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

    /// <summary>
    /// Keep the declaration order. Recognized by identity, like Python's
    /// <c>sort_signals=None</c>: only this instance makes the DBC writer preserve
    /// declaration order; any other sorter dumps by start bit, reversed.
    /// </summary>
    public static readonly SignalSort None = signals => signals;

    internal static bool KeepsDeclarationOrder(SignalSort? sort) => ReferenceEquals(sort, None);

    internal static int NetworkStartBit(Signal signal) =>
        signal.ByteOrder == ByteOrder.BigEndian
            ? BitNumbering.SawtoothToNetwork(signal.StartBit)
            : signal.StartBit;
}
