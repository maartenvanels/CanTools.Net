namespace CanTools.Transport;

/// <summary>
/// A single CAN frame as this library sees it: a frame id and its data bytes.
/// The extended/FD flags are carried so 29-bit and CAN FD support can be added
/// later without a breaking change; v1 producers set classic 11-bit frames.
/// </summary>
public readonly struct CanFrame
{
    public CanFrame(uint id, byte[] data, bool isExtended = false, bool isFd = false)
    {
        Id = id;
        Data = data ?? throw new ArgumentNullException(nameof(data));
        IsExtended = isExtended;
        IsFd = isFd;
    }

    public uint Id { get; }

    public byte[] Data { get; }

    public bool IsExtended { get; }

    public bool IsFd { get; }

    /// <summary>A classic 11-bit data frame.</summary>
    public static CanFrame Classic(uint id, byte[] data) => new(id, data);
}
