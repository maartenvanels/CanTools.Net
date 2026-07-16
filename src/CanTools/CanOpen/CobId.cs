namespace CanTools.CanOpen;

/// <summary>
/// An 11-bit CANopen communication object id: a 4-bit function code and a 7-bit
/// node id, classified per the CiA 301 predefined connection set.
/// </summary>
public readonly record struct CobId(uint Raw)
{
    private const uint LssTransmitId = 0x7E4;
    private const uint LssReceiveId = 0x7E5;

    /// <summary>The upper four bits of the 11-bit id.</summary>
    public int FunctionCode => (int)(Raw >> 7) & 0xF;

    /// <summary>The lower seven bits: the node id, 0 for broadcast objects.</summary>
    public int NodeId => (int)(Raw & 0x7F);

    public CanOpenFunction Function
    {
        get
        {
            if (Raw == LssTransmitId)
            {
                return CanOpenFunction.LssTransmit;
            }

            if (Raw == LssReceiveId)
            {
                return CanOpenFunction.LssReceive;
            }

            return FunctionCode switch
            {
                0x0 when NodeId == 0 => CanOpenFunction.Nmt,
                0x1 => NodeId == 0 ? CanOpenFunction.Sync : CanOpenFunction.Emergency,
                0x2 when NodeId == 0 => CanOpenFunction.Time,
                0x3 when NodeId != 0 => CanOpenFunction.Tpdo1,
                0x4 when NodeId != 0 => CanOpenFunction.Rpdo1,
                0x5 when NodeId != 0 => CanOpenFunction.Tpdo2,
                0x6 when NodeId != 0 => CanOpenFunction.Rpdo2,
                0x7 when NodeId != 0 => CanOpenFunction.Tpdo3,
                0x8 when NodeId != 0 => CanOpenFunction.Rpdo3,
                0x9 when NodeId != 0 => CanOpenFunction.Tpdo4,
                0xA when NodeId != 0 => CanOpenFunction.Rpdo4,
                0xB when NodeId != 0 => CanOpenFunction.SdoTransmit,
                0xC when NodeId != 0 => CanOpenFunction.SdoReceive,
                0xE when NodeId != 0 => CanOpenFunction.Heartbeat,
                _ => CanOpenFunction.Unknown,
            };
        }
    }

    /// <summary>Composes the COB-ID of a connection-set object for a node.</summary>
    public static CobId For(CanOpenFunction function, int nodeId = 0)
    {
        if (nodeId is < 0 or > 127)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nodeId), nodeId, "A node id is 0..127.");
        }

        var baseId = function switch
        {
            CanOpenFunction.Nmt => 0x000u,
            CanOpenFunction.Sync => 0x080u,
            CanOpenFunction.Emergency => 0x080u,
            CanOpenFunction.Time => 0x100u,
            CanOpenFunction.Tpdo1 => 0x180u,
            CanOpenFunction.Rpdo1 => 0x200u,
            CanOpenFunction.Tpdo2 => 0x280u,
            CanOpenFunction.Rpdo2 => 0x300u,
            CanOpenFunction.Tpdo3 => 0x380u,
            CanOpenFunction.Rpdo3 => 0x400u,
            CanOpenFunction.Tpdo4 => 0x480u,
            CanOpenFunction.Rpdo4 => 0x500u,
            CanOpenFunction.SdoTransmit => 0x580u,
            CanOpenFunction.SdoReceive => 0x600u,
            CanOpenFunction.Heartbeat => 0x700u,
            CanOpenFunction.LssTransmit => LssTransmitId,
            CanOpenFunction.LssReceive => LssReceiveId,
            _ => throw new ArgumentOutOfRangeException(nameof(function), function, null),
        };

        return new CobId(baseId + (uint)nodeId);
    }

    public override string ToString() => $"0x{Raw:X3} ({Function}, node {NodeId})";
}
