namespace CanTools.CanOpen;

/// <summary>The communication object a COB-ID belongs to (CiA 301 connection set).</summary>
public enum CanOpenFunction
{
    Unknown,
    Nmt,
    Sync,
    Emergency,
    Time,
    Tpdo1,
    Rpdo1,
    Tpdo2,
    Rpdo2,
    Tpdo3,
    Rpdo3,
    Tpdo4,
    Rpdo4,

    /// <summary>SDO server → client (0x580 + node id).</summary>
    SdoTransmit,

    /// <summary>SDO client → server (0x600 + node id).</summary>
    SdoReceive,

    /// <summary>Heartbeat, boot-up and node guarding (0x700 + node id).</summary>
    Heartbeat,

    LssTransmit,
    LssReceive,
}
