namespace CanTools.CanOpen;

/// <summary>An NMT master command frame (COB-ID 0x000): a command and a target node.</summary>
public readonly record struct NmtMessage(NmtCommand Command, int NodeId)
{
    /// <summary>Node id 0 addresses all nodes.</summary>
    public bool IsBroadcast => NodeId == 0;

    /// <summary>Whether the command addresses the given node.</summary>
    public bool Targets(int nodeId) => NodeId == 0 || NodeId == nodeId;

    public static NmtMessage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
        {
            throw CanOpenFrames.WrongLength("An NMT command", "2 data bytes", data.Length);
        }

        return new NmtMessage((NmtCommand)data[0], data[1]);
    }

    public byte[] ToBytes()
    {
        if (NodeId is < 0 or > 127)
        {
            throw new EncodeException($"A node id is 0..127, but got {NodeId}.");
        }

        return [(byte)Command, (byte)NodeId];
    }
}
