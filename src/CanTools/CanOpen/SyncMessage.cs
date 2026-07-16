namespace CanTools.CanOpen;

/// <summary>A SYNC frame (COB-ID 0x080), optionally carrying a counter.</summary>
public readonly record struct SyncMessage(byte? Counter = null)
{
    public static SyncMessage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length > 1)
        {
            throw new DecodeException(
                $"A SYNC frame has 0 or 1 data bytes, but got {data.Length}.");
        }

        return new SyncMessage(data.Length == 1 ? data[0] : null);
    }

    public byte[] ToBytes() => Counter is { } counter ? [counter] : [];
}
