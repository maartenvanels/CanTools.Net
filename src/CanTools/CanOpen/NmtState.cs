namespace CanTools.CanOpen;

/// <summary>An NMT node state as reported in heartbeat frames.</summary>
public enum NmtState : byte
{
    /// <summary>Also the state a boot-up message reports.</summary>
    Initialising = 0x00,
    Stopped = 0x04,
    Operational = 0x05,
    Sleep = 0x50,
    Standby = 0x60,
    PreOperational = 0x7F,
}
