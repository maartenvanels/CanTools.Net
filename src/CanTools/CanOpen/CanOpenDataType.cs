namespace CanTools.CanOpen;

/// <summary>CiA 301 data type codes as used in object dictionaries.</summary>
public enum CanOpenDataType
{
    Boolean = 0x01,
    Integer8 = 0x02,
    Integer16 = 0x03,
    Integer32 = 0x04,
    Unsigned8 = 0x05,
    Unsigned16 = 0x06,
    Unsigned32 = 0x07,
    Real32 = 0x08,
    VisibleString = 0x09,
    OctetString = 0x0A,
    UnicodeString = 0x0B,
    TimeOfDay = 0x0C,
    TimeDifference = 0x0D,
    Domain = 0x0F,
    Integer24 = 0x10,
    Real64 = 0x11,
    Integer40 = 0x12,
    Integer48 = 0x13,
    Integer56 = 0x14,
    Integer64 = 0x15,
    Unsigned24 = 0x16,
    Unsigned40 = 0x18,
    Unsigned48 = 0x19,
    Unsigned56 = 0x1A,
    Unsigned64 = 0x1B,
    PdoCommunicationParameter = 0x20,
    PdoMapping = 0x21,
    SdoParameter = 0x22,
    Identity = 0x23,
}

/// <summary>Classification helpers for <see cref="CanOpenDataType"/>.</summary>
public static class CanOpenDataTypes
{
    public static bool IsSigned(this CanOpenDataType type) => type is
        CanOpenDataType.Integer8 or CanOpenDataType.Integer16 or CanOpenDataType.Integer24
        or CanOpenDataType.Integer32 or CanOpenDataType.Integer40 or CanOpenDataType.Integer48
        or CanOpenDataType.Integer56 or CanOpenDataType.Integer64;

    public static bool IsUnsigned(this CanOpenDataType type) => type is
        CanOpenDataType.Unsigned8 or CanOpenDataType.Unsigned16 or CanOpenDataType.Unsigned24
        or CanOpenDataType.Unsigned32 or CanOpenDataType.Unsigned40 or CanOpenDataType.Unsigned48
        or CanOpenDataType.Unsigned56 or CanOpenDataType.Unsigned64;

    public static bool IsInteger(this CanOpenDataType type) => type.IsSigned() || type.IsUnsigned();

    public static bool IsFloat(this CanOpenDataType type) =>
        type is CanOpenDataType.Real32 or CanOpenDataType.Real64;

    /// <summary>The bit width of fixed-size types, or null for strings/domains.</summary>
    public static int? BitLength(this CanOpenDataType type) => type switch
    {
        CanOpenDataType.Boolean => 8,
        CanOpenDataType.Integer8 or CanOpenDataType.Unsigned8 => 8,
        CanOpenDataType.Integer16 or CanOpenDataType.Unsigned16 => 16,
        CanOpenDataType.Integer24 or CanOpenDataType.Unsigned24 => 24,
        CanOpenDataType.Integer32 or CanOpenDataType.Unsigned32 or CanOpenDataType.Real32 => 32,
        CanOpenDataType.Integer40 or CanOpenDataType.Unsigned40 => 40,
        CanOpenDataType.Integer48 or CanOpenDataType.Unsigned48 => 48,
        CanOpenDataType.Integer56 or CanOpenDataType.Unsigned56 => 56,
        CanOpenDataType.Integer64 or CanOpenDataType.Unsigned64 or CanOpenDataType.Real64 => 64,
        _ => null,
    };
}
