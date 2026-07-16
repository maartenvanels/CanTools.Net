namespace CanTools.CanOpen;

/// <summary>SDO abort codes (CiA 301).</summary>
public enum SdoAbortCode : uint
{
    ToggleBitNotAlternated = 0x05030000,
    TimedOut = 0x05040000,
    InvalidCommandSpecifier = 0x05040001,
    InvalidBlockSize = 0x05040002,
    InvalidSequenceNumber = 0x05040003,
    CrcError = 0x05040004,
    OutOfMemory = 0x05040005,
    UnsupportedAccess = 0x06010000,
    ReadWriteOnlyObject = 0x06010001,
    WriteReadOnlyObject = 0x06010002,
    ObjectDoesNotExist = 0x06020000,
    ObjectCannotBeMapped = 0x06040041,
    PdoLengthExceeded = 0x06040042,
    ParameterIncompatibility = 0x06040043,
    InternalIncompatibility = 0x06040047,
    HardwareError = 0x06060000,
    LengthDoesNotMatch = 0x06070010,
    LengthTooHigh = 0x06070012,
    LengthTooLow = 0x06070013,
    SubindexDoesNotExist = 0x06090011,
    InvalidValue = 0x06090030,
    ValueTooHigh = 0x06090031,
    ValueTooLow = 0x06090032,
    MaximumLessThanMinimum = 0x06090036,
    ResourceNotAvailable = 0x060A0023,
    GeneralError = 0x08000000,
    CannotStoreToApplication = 0x08000020,
    CannotStoreLocalControl = 0x08000021,
    CannotStoreDeviceState = 0x08000022,
    ObjectDictionaryGenerationFailed = 0x08000023,
    NoDataAvailable = 0x08000024,
}

/// <summary>Descriptions for <see cref="SdoAbortCode"/>.</summary>
public static class SdoAbortCodes
{
    /// <summary>The CiA 301 description, or an empty string for unassigned codes.</summary>
    public static string Description(this SdoAbortCode code) => code switch
    {
        SdoAbortCode.ToggleBitNotAlternated => "SDO toggle bit error",
        SdoAbortCode.TimedOut => "Timeout of transfer communication detected",
        SdoAbortCode.InvalidCommandSpecifier => "Unknown SDO command specified",
        SdoAbortCode.InvalidBlockSize => "Invalid block size",
        SdoAbortCode.InvalidSequenceNumber => "Invalid sequence number",
        SdoAbortCode.CrcError => "CRC error",
        SdoAbortCode.OutOfMemory => "Out of memory",
        SdoAbortCode.UnsupportedAccess => "Unsupported access to an object",
        SdoAbortCode.ReadWriteOnlyObject => "Attempt to read a write only object",
        SdoAbortCode.WriteReadOnlyObject => "Attempt to write a read only object",
        SdoAbortCode.ObjectDoesNotExist => "Object does not exist",
        SdoAbortCode.ObjectCannotBeMapped => "Object cannot be mapped to the PDO",
        SdoAbortCode.PdoLengthExceeded => "PDO length exceeded",
        SdoAbortCode.ParameterIncompatibility => "General parameter incompatibility reason",
        SdoAbortCode.InternalIncompatibility => "General internal incompatibility in the device",
        SdoAbortCode.HardwareError => "Access failed due to a hardware error",
        SdoAbortCode.LengthDoesNotMatch => "Data type and length code do not match",
        SdoAbortCode.LengthTooHigh => "Data type does not match, length of service parameter too high",
        SdoAbortCode.LengthTooLow => "Data type does not match, length of service parameter too low",
        SdoAbortCode.SubindexDoesNotExist => "Subindex does not exist",
        SdoAbortCode.InvalidValue => "Value range of parameter exceeded",
        SdoAbortCode.ValueTooHigh => "Value of parameter written too high",
        SdoAbortCode.ValueTooLow => "Value of parameter written too low",
        SdoAbortCode.MaximumLessThanMinimum => "Maximum value is less than minimum value",
        SdoAbortCode.ResourceNotAvailable => "Resource not available",
        SdoAbortCode.GeneralError => "General error",
        SdoAbortCode.CannotStoreToApplication => "Data cannot be transferred or stored to the application",
        SdoAbortCode.CannotStoreLocalControl =>
            "Data can not be transferred or stored to the application because of local control",
        SdoAbortCode.CannotStoreDeviceState =>
            "Data can not be transferred or stored to the application because of the present device state",
        SdoAbortCode.ObjectDictionaryGenerationFailed =>
            "Object dictionary dynamic generation fails or no object dictionary is present",
        SdoAbortCode.NoDataAvailable => "No data available",
        _ => "",
    };
}
