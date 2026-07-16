namespace CanTools;

/// <summary>Bit layout of a signal within a frame.</summary>
public enum ByteOrder
{
    /// <summary>Intel byte order: the start bit is the position of the least significant bit.</summary>
    LittleEndian,

    /// <summary>Motorola byte order: the start bit is the position of the most significant bit.</summary>
    BigEndian,
}
