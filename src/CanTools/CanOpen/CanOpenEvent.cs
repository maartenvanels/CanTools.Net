using CanTools.Logs;
using CanTools.Model;

namespace CanTools.CanOpen;

/// <summary>
/// A typed CANopen event folded out of a CAN log stream by
/// <see cref="CanOpenLogInterpreter"/>. Every event keeps the log entry that
/// completed it, so timestamps and channels stay available.
/// </summary>
public abstract record CanOpenEvent(LogEntry Entry);

/// <summary>An NMT master command.</summary>
public sealed record NmtCommandEvent(LogEntry Entry, NmtMessage Nmt) : CanOpenEvent(Entry);

/// <summary>A node announced itself after initialisation.</summary>
public sealed record BootUpEvent(LogEntry Entry, int NodeId) : CanOpenEvent(Entry);

/// <summary>A heartbeat or node guarding response.</summary>
public sealed record HeartbeatEvent(
    LogEntry Entry, int NodeId, HeartbeatMessage Heartbeat, NmtState? PreviousState) : CanOpenEvent(Entry)
{
    /// <summary>True when the reported state differs from the node's last known state.</summary>
    public bool IsStateChange => PreviousState != Heartbeat.State;
}

/// <summary>An emergency message.</summary>
public sealed record EmergencyEvent(
    LogEntry Entry, int NodeId, EmergencyMessage Emergency) : CanOpenEvent(Entry);

/// <summary>A SYNC frame.</summary>
public sealed record SyncEvent(LogEntry Entry, SyncMessage Sync) : CanOpenEvent(Entry);

/// <summary>A TIME frame.</summary>
public sealed record TimeEvent(LogEntry Entry, TimeMessage Time) : CanOpenEvent(Entry);

/// <summary>A PDO decoded through the projected database.</summary>
public sealed record PdoEvent(
    LogEntry Entry, Message Message, Dictionary<string, SignalValue> Signals) : CanOpenEvent(Entry);

/// <summary>A completed SDO write to the node, reassembled when segmented.</summary>
public sealed record SdoDownloadEvent(
    LogEntry Entry, int NodeId, ushort Index, byte Subindex, byte[] Data) : CanOpenEvent(Entry);

/// <summary>A completed SDO read from the node, reassembled when segmented.</summary>
public sealed record SdoUploadEvent(
    LogEntry Entry, int NodeId, ushort Index, byte Subindex, byte[] Data) : CanOpenEvent(Entry);

/// <summary>An SDO transfer was aborted by either side.</summary>
public sealed record SdoAbortEvent(
    LogEntry Entry, int NodeId, SdoDirection Direction, SdoAbort Abort) : CanOpenEvent(Entry);
