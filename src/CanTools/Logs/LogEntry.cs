namespace CanTools.Logs;

/// <summary>One parsed CAN frame from a log file.</summary>
public sealed class LogEntry
{
    public LogEntry(
        string channel,
        uint frameId,
        bool isExtendedFrame,
        byte[] data,
        bool isRemoteFrame,
        TimestampFormat timestampFormat,
        DateTime? timestamp = null,
        TimeSpan? timeOffset = null)
    {
        Channel = channel;
        FrameId = frameId;
        IsExtendedFrame = isExtendedFrame;
        Data = data;
        IsRemoteFrame = isRemoteFrame;
        TimestampFormat = timestampFormat;
        Timestamp = timestamp;
        TimeOffset = timeOffset;
    }

    /// <summary>The channel the frame was seen on, e.g. "can0" or "pcan1".</summary>
    public string Channel { get; }

    public uint FrameId { get; }

    public bool IsExtendedFrame { get; }

    public byte[] Data { get; }

    public bool IsRemoteFrame { get; }

    public TimestampFormat TimestampFormat { get; }

    /// <summary>The wall-clock time when <see cref="TimestampFormat"/> is Absolute.</summary>
    public DateTime? Timestamp { get; }

    /// <summary>The time since log start when <see cref="TimestampFormat"/> is Relative.</summary>
    public TimeSpan? TimeOffset { get; }
}
