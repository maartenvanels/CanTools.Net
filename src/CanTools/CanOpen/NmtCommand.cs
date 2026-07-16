namespace CanTools.CanOpen;

/// <summary>An NMT command specifier (CiA 301; Sleep and Standby come from CiA 447).</summary>
public enum NmtCommand : byte
{
    Start = 0x01,
    Stop = 0x02,
    Sleep = 0x50,
    Standby = 0x60,
    EnterPreOperational = 0x80,
    ResetNode = 0x81,
    ResetCommunication = 0x82,
}

/// <summary>Classification helpers for <see cref="NmtCommand"/>.</summary>
public static class NmtCommands
{
    /// <summary>The state a node enters after the command, or null for unknown commands.</summary>
    public static NmtState? TargetState(this NmtCommand command) => command switch
    {
        NmtCommand.Start => NmtState.Operational,
        NmtCommand.Stop => NmtState.Stopped,
        NmtCommand.Sleep => NmtState.Sleep,
        NmtCommand.Standby => NmtState.Standby,
        NmtCommand.EnterPreOperational => NmtState.PreOperational,
        NmtCommand.ResetNode or NmtCommand.ResetCommunication => NmtState.Initialising,
        _ => null,
    };
}
