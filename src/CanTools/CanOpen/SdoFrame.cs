using System.Buffers.Binary;

namespace CanTools.CanOpen;

/// <summary>
/// One SDO frame, parsed statelessly. Initiate frames, expedited transfers,
/// segments and aborts get typed representations; block transfer protocol frames
/// are only classified, because everything past a block initiate is sequencing
/// state (the data frames inside a block do not even carry a command specifier).
/// </summary>
public abstract record SdoFrame
{
    private protected SdoFrame()
    {
    }

    /// <summary>Parses one SDO frame. The direction determines what the command bits mean.</summary>
    public static SdoFrame Parse(SdoDirection direction, ReadOnlySpan<byte> data)
    {
        if (data.Length < 1)
        {
            throw new DecodeException("An SDO frame has at least 1 data byte, but got 0.");
        }

        var command = data[0];
        var toggle = (command & 0x10) != 0;

        // The command specifier lives in bits 5-7; its meaning depends on direction.
        return (direction, command & 0xE0) switch
        {
            (SdoDirection.Request, 0x00) => SdoDownloadSegmentRequest.ParseFrame(data),
            (SdoDirection.Request, 0x20) => SdoDownloadRequest.ParseFrame(data),
            (SdoDirection.Request, 0x40) => SdoUploadRequest.ParseFrame(data),
            (SdoDirection.Request, 0x60) => new SdoUploadSegmentRequest(toggle),
            (_, 0x80) => SdoAbort.ParseFrame(data),
            (SdoDirection.Request, 0xA0) => new SdoBlockFrame(direction, SdoBlockTransfer.Upload, data.ToArray()),
            (SdoDirection.Request, 0xC0) => new SdoBlockFrame(direction, SdoBlockTransfer.Download, data.ToArray()),
            (SdoDirection.Response, 0x00) => SdoUploadSegmentResponse.ParseFrame(data),
            (SdoDirection.Response, 0x20) => new SdoDownloadSegmentResponse(toggle),
            (SdoDirection.Response, 0x40) => SdoUploadResponse.ParseFrame(data),
            (SdoDirection.Response, 0x60) => SdoDownloadResponse.ParseFrame(data),
            (SdoDirection.Response, 0xA0) => new SdoBlockFrame(direction, SdoBlockTransfer.Download, data.ToArray()),
            (SdoDirection.Response, 0xC0) => new SdoBlockFrame(direction, SdoBlockTransfer.Upload, data.ToArray()),
            _ => throw new DecodeException(
                $"Unknown SDO command specifier 0x{command & 0xE0:X2}."),
        };
    }

    private protected static void EnsureLength(ReadOnlySpan<byte> data, int required)
    {
        if (data.Length < required)
        {
            throw new DecodeException(
                $"The SDO frame needs {required} data bytes, but got {data.Length}.");
        }
    }

    private protected static (ushort Index, byte Subindex) ReadHeader(ReadOnlySpan<byte> data)
    {
        EnsureLength(data, 4);

        return (BinaryPrimitives.ReadUInt16LittleEndian(data[1..]), data[3]);
    }

    private protected static byte[] BuildHeader(byte command, ushort index, byte subindex)
    {
        var frame = new byte[8];
        frame[0] = command;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), index);
        frame[3] = subindex;

        return frame;
    }

    // The shared e/s/n payload encoding of initiate download requests and initiate
    // upload responses: expedited data in bytes 4-7 (byte count from n when the size
    // bit is set), or the announced total size of a segmented transfer as uint32.
    private protected static (byte[]? ExpeditedData, bool SizeSpecified, uint? Size) ParseInitiatePayload(
        ReadOnlySpan<byte> data)
    {
        var command = data[0];

        if ((command & 0x02) != 0)
        {
            if ((command & 0x01) != 0)
            {
                var count = 4 - ((command >> 2) & 0x3);
                EnsureLength(data, 4 + count);

                return (data.Slice(4, count).ToArray(), true, null);
            }

            EnsureLength(data, 8);

            return (data.Slice(4, 4).ToArray(), false, null);
        }

        if ((command & 0x01) != 0)
        {
            EnsureLength(data, 8);

            return (null, true, BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
        }

        return (null, false, null);
    }

    private protected static byte[] BuildInitiate(
        byte baseCommand, ushort index, byte subindex, byte[]? expedited, bool sizeSpecified, uint? size)
    {
        var frame = BuildHeader(baseCommand, index, subindex);

        if (expedited is not null)
        {
            if (expedited.Length > 4)
            {
                throw new EncodeException(
                    $"Expedited SDO data is at most 4 bytes, but got {expedited.Length}.");
            }

            if (!sizeSpecified && expedited.Length != 4)
            {
                throw new EncodeException(
                    "An expedited transfer without a size indication uses all 4 data bytes.");
            }

            frame[0] |= 0x02;

            if (sizeSpecified)
            {
                frame[0] |= (byte)(0x01 | (4 - expedited.Length) << 2);
            }

            expedited.CopyTo(frame, 4);
        }
        else if (size is { } announced)
        {
            frame[0] |= 0x01;
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), announced);
        }

        return frame;
    }

    // Builds a block-transfer initiate frame: command byte, multiplexer, and a
    // uint32 in bytes 4-7 (the announced size for downloads; zero for uploads).
    internal static byte[] BuildBlockInitiate(byte command, ushort index, byte subindex, uint size)
    {
        var frame = BuildHeader(command, index, subindex);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), size);

        return frame;
    }

    private protected static byte[] BuildSegment(byte baseCommand, bool toggle, byte[] payload, bool isLast)
    {
        if (payload.Length > 7)
        {
            throw new EncodeException(
                $"An SDO segment carries at most 7 bytes, but got {payload.Length}.");
        }

        var frame = new byte[8];
        frame[0] = (byte)(baseCommand
                          | (toggle ? 0x10 : 0)
                          | (7 - payload.Length) << 1
                          | (isLast ? 0x01 : 0));
        payload.CopyTo(frame, 1);

        return frame;
    }
}

/// <summary>An initiate upload request: asks the server for the value of an entry.</summary>
public sealed record SdoUploadRequest(ushort Index, byte Subindex) : SdoFrame
{
    internal static SdoUploadRequest ParseFrame(ReadOnlySpan<byte> data)
    {
        var (index, subindex) = ReadHeader(data);

        return new SdoUploadRequest(index, subindex);
    }

    public byte[] ToBytes() => BuildHeader(0x40, Index, Subindex);
}

/// <summary>
/// An initiate upload response. An expedited transfer carries the value in
/// <see cref="ExpeditedData"/>; a segmented transfer only announces its
/// <see cref="Size"/> here, the value follows in segments.
/// </summary>
public sealed record SdoUploadResponse(ushort Index, byte Subindex) : SdoFrame
{
    public SdoUploadResponse(ushort index, byte subindex, byte[] expeditedData)
        : this(index, subindex)
    {
        ExpeditedData = expeditedData;
        SizeSpecified = true;
    }

    /// <summary>The expedited payload, or null when the transfer is segmented.</summary>
    public byte[]? ExpeditedData { get; init; }

    /// <summary>Whether the frame states how many bytes are valid.</summary>
    public bool SizeSpecified { get; init; }

    /// <summary>The announced total byte count of a segmented transfer.</summary>
    public uint? Size { get; init; }

    public bool IsExpedited => ExpeditedData is not null;

    internal static SdoUploadResponse ParseFrame(ReadOnlySpan<byte> data)
    {
        var (index, subindex) = ReadHeader(data);
        var (expedited, sizeSpecified, size) = ParseInitiatePayload(data);

        return new SdoUploadResponse(index, subindex)
        {
            ExpeditedData = expedited,
            SizeSpecified = sizeSpecified,
            Size = size,
        };
    }

    public byte[] ToBytes() => BuildInitiate(0x40, Index, Subindex, ExpeditedData, SizeSpecified, Size);
}

/// <summary>
/// An initiate download request: writes a value. An expedited transfer carries up
/// to four data bytes; a segmented transfer only announces its <see cref="Size"/>.
/// </summary>
public sealed record SdoDownloadRequest(ushort Index, byte Subindex) : SdoFrame
{
    public SdoDownloadRequest(ushort index, byte subindex, byte[] expeditedData)
        : this(index, subindex)
    {
        ExpeditedData = expeditedData;
        SizeSpecified = true;
    }

    /// <summary>The expedited payload, or null when the transfer is segmented.</summary>
    public byte[]? ExpeditedData { get; init; }

    /// <summary>Whether the frame states how many bytes are valid.</summary>
    public bool SizeSpecified { get; init; }

    /// <summary>The announced total byte count of a segmented transfer.</summary>
    public uint? Size { get; init; }

    public bool IsExpedited => ExpeditedData is not null;

    internal static SdoDownloadRequest ParseFrame(ReadOnlySpan<byte> data)
    {
        var (index, subindex) = ReadHeader(data);
        var (expedited, sizeSpecified, size) = ParseInitiatePayload(data);

        return new SdoDownloadRequest(index, subindex)
        {
            ExpeditedData = expedited,
            SizeSpecified = sizeSpecified,
            Size = size,
        };
    }

    public byte[] ToBytes() => BuildInitiate(0x20, Index, Subindex, ExpeditedData, SizeSpecified, Size);
}

/// <summary>An initiate download response: the server accepted the write.</summary>
public sealed record SdoDownloadResponse(ushort Index, byte Subindex) : SdoFrame
{
    internal static SdoDownloadResponse ParseFrame(ReadOnlySpan<byte> data)
    {
        var (index, subindex) = ReadHeader(data);

        return new SdoDownloadResponse(index, subindex);
    }

    public byte[] ToBytes() => BuildHeader(0x60, Index, Subindex);
}

/// <summary>An upload segment request: asks the server for the next segment.</summary>
public sealed record SdoUploadSegmentRequest(bool Toggle) : SdoFrame
{
    public byte[] ToBytes() => [(byte)(0x60 | (Toggle ? 0x10 : 0)), 0, 0, 0, 0, 0, 0, 0];
}

/// <summary>An upload segment response: up to seven payload bytes.</summary>
public sealed record SdoUploadSegmentResponse(bool Toggle, byte[] Data, bool IsLast) : SdoFrame
{
    internal static SdoUploadSegmentResponse ParseFrame(ReadOnlySpan<byte> data)
    {
        var command = data[0];
        var count = 7 - ((command >> 1) & 0x7);
        EnsureLength(data, 1 + count);

        return new SdoUploadSegmentResponse(
            (command & 0x10) != 0, data.Slice(1, count).ToArray(), (command & 0x01) != 0);
    }

    public byte[] ToBytes() => BuildSegment(0x00, Toggle, Data, IsLast);
}

/// <summary>A download segment request: up to seven payload bytes.</summary>
public sealed record SdoDownloadSegmentRequest(bool Toggle, byte[] Data, bool IsLast) : SdoFrame
{
    internal static SdoDownloadSegmentRequest ParseFrame(ReadOnlySpan<byte> data)
    {
        var command = data[0];
        var count = 7 - ((command >> 1) & 0x7);
        EnsureLength(data, 1 + count);

        return new SdoDownloadSegmentRequest(
            (command & 0x10) != 0, data.Slice(1, count).ToArray(), (command & 0x01) != 0);
    }

    public byte[] ToBytes() => BuildSegment(0x00, Toggle, Data, IsLast);
}

/// <summary>A download segment response: the server accepted the segment.</summary>
public sealed record SdoDownloadSegmentResponse(bool Toggle) : SdoFrame
{
    public byte[] ToBytes() => [(byte)(0x20 | (Toggle ? 0x10 : 0)), 0, 0, 0, 0, 0, 0, 0];
}

/// <summary>An SDO abort, sent by either side to cancel a transfer.</summary>
public sealed record SdoAbort(ushort Index, byte Subindex, SdoAbortCode Code) : SdoFrame
{
    public string Description => Code.Description();

    internal static SdoAbort ParseFrame(ReadOnlySpan<byte> data)
    {
        var (index, subindex) = ReadHeader(data);
        EnsureLength(data, 8);

        return new SdoAbort(
            index, subindex, (SdoAbortCode)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]));
    }

    public byte[] ToBytes()
    {
        var frame = BuildHeader(0x80, Index, Subindex);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), (uint)Code);

        return frame;
    }

    // matches python-canopen's SdoAbortedError formatting
    public override string ToString() =>
        CanOpenFrames.Describe($"Code 0x{(uint)Code:X8}", Description);
}

/// <summary>Which value a block transfer moves.</summary>
public enum SdoBlockTransfer
{
    Download,
    Upload,
}

/// <summary>
/// A block transfer protocol frame. The wire layout of the command byte and the
/// initiate header is exposed here; block sequencing is stateful and belongs to
/// the log interpreter.
/// </summary>
public sealed record SdoBlockFrame(SdoDirection Direction, SdoBlockTransfer Transfer, byte[] Data) : SdoFrame
{
    /// <summary>The command specifier byte.</summary>
    public byte Command => Data[0];

    /// <summary>
    /// Whether the frame comes from the side that carries the payload — the client
    /// for downloads, the server for uploads (the 0xC0 command family). The other
    /// side sends the 0xA0 family: initiate acks, the upload start, segment acks
    /// and end acks.
    /// </summary>
    public bool IsCarrierSide =>
        Direction == (Transfer == SdoBlockTransfer.Download
            ? SdoDirection.Request
            : SdoDirection.Response);

    /// <summary>Carrier side: whether this is the end frame instead of an initiate.</summary>
    public bool IsEnd => (Command & 0x01) != 0;

    /// <summary>The number of padding bytes in the last segment, from an end frame.</summary>
    public int PaddingCount => (Command >> 2) & 0x7;

    /// <summary>Receiver side: the sub-command in bits 0-1.</summary>
    public int SubCommand => Command & 0x03;

    /// <summary>The acknowledged sequence number of a segment ack.</summary>
    public byte AckSequence => Data[1];

    /// <summary>The index and subindex of an initiate frame.</summary>
    public (ushort Index, byte Subindex) Multiplexer => ReadHeader(Data);
}
