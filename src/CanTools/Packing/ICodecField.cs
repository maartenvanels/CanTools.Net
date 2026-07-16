namespace CanTools.Packing;

/// <summary>
/// The bit-layout description of a field within a frame. Implemented by signals and
/// diagnostic data objects; <see cref="MessageCodec"/> only depends on this surface.
/// </summary>
public interface ICodecField
{
    string Name { get; }

    /// <summary>DBC start bit: LSB position for little endian, MSB position for big endian.</summary>
    int StartBit { get; }

    /// <summary>Size of the field in bits.</summary>
    int Length { get; }

    ByteOrder ByteOrder { get; }

    bool IsSigned { get; }

    Conversion Conversion { get; }
}
