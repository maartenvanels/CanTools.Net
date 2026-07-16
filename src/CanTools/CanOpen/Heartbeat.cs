namespace CanTools.CanOpen;

/// <summary>A heartbeat, boot-up or node guarding response frame (COB-ID 0x700 + node id).</summary>
public readonly record struct Heartbeat(NmtState State, bool Toggle = false)
{
    /// <summary>True for the boot-up message a node sends after initialisation.</summary>
    public bool IsBootUp => State == NmtState.Initialising;

    public static Heartbeat Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            throw new DecodeException("A heartbeat frame has 1 data byte, but got 0.");
        }

        // Bit 7 is the node guarding toggle bit, not part of the state.
        return new Heartbeat((NmtState)(data[0] & 0x7F), (data[0] & 0x80) != 0);
    }

    public byte[] ToBytes() => [(byte)((byte)State | (Toggle ? 0x80 : 0))];
}
