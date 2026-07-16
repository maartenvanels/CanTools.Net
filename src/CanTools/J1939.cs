namespace CanTools;

/// <summary>J1939 frame id and parameter group number helpers.</summary>
public static class J1939
{
    public static bool IsPduFormat1(int pduFormat) => pduFormat < 240;

    /// <summary>Packs the given fields into a 29-bit frame id.</summary>
    public static uint FrameIdPack(
        int priority,
        int reserved,
        int dataPage,
        int pduFormat,
        int pduSpecific,
        int sourceAddress)
    {
        if (priority is < 0 or > 7)
        {
            throw new CanToolsException($"Expected priority 0..7, but got {priority}.");
        }

        if (reserved is < 0 or > 1)
        {
            throw new CanToolsException($"Expected reserved 0..1, but got {reserved}.");
        }

        if (dataPage is < 0 or > 1)
        {
            throw new CanToolsException($"Expected data page 0..1, but got {dataPage}.");
        }

        if (pduFormat is < 0 or > 255)
        {
            throw new CanToolsException($"Expected PDU format 0..255, but got {pduFormat}.");
        }

        if (pduSpecific is < 0 or > 255)
        {
            throw new CanToolsException($"Expected PDU specific 0..255, but got {pduSpecific}.");
        }

        if (sourceAddress is < 0 or > 255)
        {
            throw new CanToolsException($"Expected source address 0..255, but got {sourceAddress}.");
        }

        return (uint)(priority << 26 | reserved << 25 | dataPage << 24
                      | pduFormat << 16 | pduSpecific << 8 | sourceAddress);
    }

    /// <summary>Unpacks a 29-bit frame id into its fields.</summary>
    public static J1939FrameId FrameIdUnpack(long frameId)
    {
        if (frameId is < 0 or > 0x1fffffff)
        {
            throw new CanToolsException(
                $"Expected a frame id 0..0x1fffffff, but got 0x{frameId:x}.");
        }

        return new J1939FrameId(
            Priority: (int)(frameId >> 26) & 0x7,
            Reserved: (int)(frameId >> 25) & 0x1,
            DataPage: (int)(frameId >> 24) & 0x1,
            PduFormat: (int)(frameId >> 16) & 0xff,
            PduSpecific: (int)(frameId >> 8) & 0xff,
            SourceAddress: (int)frameId & 0xff);
    }

    /// <summary>Packs the given fields into an 18-bit parameter group number.</summary>
    public static uint PgnPack(int reserved, int dataPage, int pduFormat, int pduSpecific = 0)
    {
        if (pduFormat < 240 && pduSpecific != 0)
        {
            throw new CanToolsException(
                $"Expected PDU specific 0 when PDU format is 0..239, but got {pduSpecific}.");
        }

        if (reserved is < 0 or > 1)
        {
            throw new CanToolsException($"Expected reserved 0..1, but got {reserved}.");
        }

        if (dataPage is < 0 or > 1)
        {
            throw new CanToolsException($"Expected data page 0..1, but got {dataPage}.");
        }

        if (pduFormat is < 0 or > 255)
        {
            throw new CanToolsException($"Expected PDU format 0..255, but got {pduFormat}.");
        }

        if (pduSpecific is < 0 or > 255)
        {
            throw new CanToolsException($"Expected PDU specific 0..255, but got {pduSpecific}.");
        }

        return (uint)(reserved << 17 | dataPage << 16 | pduFormat << 8 | pduSpecific);
    }

    /// <summary>Unpacks an 18-bit parameter group number into its fields.</summary>
    public static J1939Pgn PgnUnpack(long pgn)
    {
        if (pgn is < 0 or > 0x3ffff)
        {
            throw new CanToolsException(
                $"Expected a parameter group number 0..0x3ffff, but got 0x{pgn:x}.");
        }

        return new J1939Pgn(
            Reserved: (int)(pgn >> 17) & 0x1,
            DataPage: (int)(pgn >> 16) & 0x1,
            PduFormat: (int)(pgn >> 8) & 0xff,
            PduSpecific: (int)pgn & 0xff);
    }

    /// <summary>The parameter group number of the given frame id.</summary>
    public static uint PgnFromFrameId(long frameId)
    {
        var unpacked = FrameIdUnpack(frameId);
        var pduSpecific = unpacked.PduFormat < 240 ? 0 : unpacked.PduSpecific;

        return PgnPack(unpacked.Reserved, unpacked.DataPage, unpacked.PduFormat, pduSpecific);
    }
}
