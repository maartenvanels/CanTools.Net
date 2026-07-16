namespace CanTools.CanOpen;

/// <summary>A SYNC frame (COB-ID 0x080), optionally carrying a counter.</summary>
public readonly record struct SyncMessage(byte? Counter = null)
{
    public static SyncMessage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length > 1)
        {
            throw CanOpenFrames.WrongLength("A SYNC", "0 or 1 data bytes", data.Length);
        }

        return new SyncMessage(data.Length == 1 ? data[0] : null);
    }

    public byte[] ToBytes() => Counter is { } counter ? [counter] : [];
}
