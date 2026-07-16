using System.Buffers.Binary;

namespace CanTools.CanOpen;

/// <summary>
/// An EMCY frame (COB-ID 0x080 + node id): a 16-bit error code, the error register
/// and five manufacturer-specific bytes.
/// </summary>
public readonly struct EmergencyMessage
{
    // The CiA 301 error classes; the first matching entry wins.
    private static readonly (ushort Code, ushort Mask, string Description)[] Descriptions =
    [
        (0x0000, 0xFF00, "Error Reset / No Error"),
        (0x1000, 0xFF00, "Generic Error"),
        (0x2000, 0xF000, "Current"),
        (0x3000, 0xF000, "Voltage"),
        (0x4000, 0xF000, "Temperature"),
        (0x5000, 0xFF00, "Device Hardware"),
        (0x6000, 0xF000, "Device Software"),
        (0x7000, 0xFF00, "Additional Modules"),
        (0x8000, 0xF000, "Monitoring"),
        (0x9000, 0xFF00, "External Error"),
        (0xF000, 0xFF00, "Additional Functions"),
        (0xFF00, 0xFF00, "Device Specific"),
    ];

    private readonly byte[]? _vendorData;

    public EmergencyMessage(ushort errorCode, byte errorRegister = 0, ReadOnlySpan<byte> vendorData = default)
    {
        if (vendorData.Length > 5)
        {
            throw new ArgumentException(
                $"EMCY vendor data is at most 5 bytes, but got {vendorData.Length}.",
                nameof(vendorData));
        }

        ErrorCode = errorCode;
        ErrorRegister = errorRegister;
        var padded = new byte[5];
        vendorData.CopyTo(padded);
        _vendorData = padded;
    }

    public ushort ErrorCode { get; }

    public byte ErrorRegister { get; }

    /// <summary>The five manufacturer-specific bytes, zero padded.</summary>
    public ReadOnlyMemory<byte> VendorData => _vendorData ?? new byte[5];

    /// <summary>Error codes 0x00xx signal that previous errors are reset.</summary>
    public bool IsErrorReset => (ErrorCode & 0xFF00) == 0;

    /// <summary>The CiA 301 error class, or an empty string for unassigned codes.</summary>
    public string Description => DescriptionOf(ErrorCode);

    public static string DescriptionOf(ushort errorCode)
    {
        foreach (var (code, mask, description) in Descriptions)
        {
            if ((errorCode & mask) == code)
            {
                return description;
            }
        }

        return "";
    }

    public static EmergencyMessage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
        {
            throw new DecodeException($"An EMCY frame has 8 data bytes, but got {data.Length}.");
        }

        return new EmergencyMessage(
            BinaryPrimitives.ReadUInt16LittleEndian(data), data[2], data.Slice(3, 5));
    }

    public byte[] ToBytes()
    {
        var frame = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, ErrorCode);
        frame[2] = ErrorRegister;
        _vendorData?.CopyTo(frame.AsSpan(3));

        return frame;
    }

    // matches python-canopen's EmcyError formatting
    public override string ToString()
    {
        var description = Description;

        return description.Length == 0
            ? $"Code 0x{ErrorCode:X4}"
            : $"Code 0x{ErrorCode:X4}, {description}";
    }
}
