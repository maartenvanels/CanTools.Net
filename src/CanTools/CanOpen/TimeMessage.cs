using System.Buffers.Binary;

namespace CanTools.CanOpen;

/// <summary>
/// A TIME frame (COB-ID 0x100): days since the CANopen epoch (1984-01-01) and
/// milliseconds since midnight, both little-endian.
/// </summary>
public readonly record struct TimeMessage(int Days, uint MillisecondsOfDay)
{
    public static readonly DateTimeOffset Epoch = new(1984, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Timestamp => Epoch.AddDays(Days).AddMilliseconds(MillisecondsOfDay);

    public static TimeMessage From(DateTimeOffset timestamp)
    {
        var elapsed = timestamp - Epoch;

        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timestamp), timestamp, "The CANopen epoch starts at 1984-01-01.");
        }

        return new TimeMessage(
            (int)(elapsed.Ticks / TimeSpan.TicksPerDay),
            (uint)(elapsed.Ticks % TimeSpan.TicksPerDay / TimeSpan.TicksPerMillisecond));
    }

    public static TimeMessage Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 6)
        {
            throw new DecodeException($"A TIME frame has 6 data bytes, but got {data.Length}.");
        }

        return new TimeMessage(
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(data));
    }

    public byte[] ToBytes()
    {
        if (Days is < 0 or > ushort.MaxValue)
        {
            throw new EncodeException($"TIME days are 0..65535, but got {Days}.");
        }

        var frame = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(frame, MillisecondsOfDay);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4), (ushort)Days);

        return frame;
    }
}
