using System.Buffers.Binary;
using System.Text;

namespace CanTools.CanOpen;

/// <summary>
/// Converts between the raw bytes an SDO transfer moves and a typed
/// <see cref="OdValue"/>, using the CiA 301 data type of the entry.
/// </summary>
public static class SdoValueCodec
{
    public static OdValue Decode(byte[] raw, CanOpenDataType type)
    {
        ArgumentNullException.ThrowIfNull(raw);

        if (type == CanOpenDataType.VisibleString || type == CanOpenDataType.UnicodeString)
        {
            return Encoding.UTF8.GetString(raw).TrimEnd('\0');
        }

        if (type.IsFloat())
        {
            return type == CanOpenDataType.Real32
                ? BitConverter.ToSingle(Pad(raw, 4))
                : BitConverter.ToDouble(Pad(raw, 8));
        }

        if (type.IsUnsigned() || type == CanOpenDataType.Boolean)
        {
            return BinaryPrimitives.ReadUInt64LittleEndian(Pad(raw, 8));
        }

        if (type.IsSigned())
        {
            var bits = (type.BitLength() ?? 8);
            var value = (long)BinaryPrimitives.ReadUInt64LittleEndian(Pad(raw, 8));
            // sign-extend from the type's bit width
            var shift = 64 - bits;
            return (value << shift) >> shift;
        }

        // OctetString, Domain, the time types and the composite records
        // (PDO/SDO parameters, Identity): return the opaque bytes unchanged.
        return raw;
    }

    public static byte[] Encode(OdValue value, CanOpenDataType type)
    {
        if (type == CanOpenDataType.VisibleString || type == CanOpenDataType.UnicodeString)
        {
            return Encoding.UTF8.GetBytes(value.Text ?? throw new EncodeException(
                "A string SDO value is required for a string data type."));
        }

        if (type.IsFloat())
        {
            return type == CanOpenDataType.Real32
                ? BitConverter.GetBytes((float)value.ToDouble())
                : BitConverter.GetBytes(value.ToDouble());
        }

        var byteCount = (type.BitLength() ?? 8) / 8;

        if (type.IsInteger() || type == CanOpenDataType.Boolean)
        {
            Span<byte> buffer = stackalloc byte[8];
            var raw = type.IsSigned()
                ? unchecked((ulong)value.ToInt64())
                : value.ToUInt64();
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, raw);
            return buffer[..byteCount].ToArray();
        }

        return value.Bytes ?? throw new EncodeException(
            $"Cannot encode an SDO value for data type {type}.");
    }

    private static byte[] Pad(byte[] raw, int length)
    {
        if (raw.Length >= length)
        {
            return raw;
        }

        var padded = new byte[length];
        raw.CopyTo(padded, 0);
        return padded;
    }
}

/// <summary>Typed convenience over <see cref="SdoClient"/>.</summary>
public static class SdoClientTypedExtensions
{
    public static async Task<OdValue> UploadAsync(
        this SdoClient client, ushort index, byte subIndex, CanOpenDataType type,
        CancellationToken cancellationToken = default) =>
        SdoValueCodec.Decode(await client.UploadAsync(index, subIndex, cancellationToken), type);

    public static Task DownloadAsync(
        this SdoClient client, ushort index, byte subIndex, OdValue value, CanOpenDataType type,
        CancellationToken cancellationToken = default) =>
        client.DownloadAsync(index, subIndex, SdoValueCodec.Encode(value, type), cancellationToken);
}
