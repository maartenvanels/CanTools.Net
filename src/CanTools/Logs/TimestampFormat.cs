namespace CanTools.Logs;

/// <summary>The kind of timestamp a log line carries.</summary>
public enum TimestampFormat
{
    /// <summary>Wall-clock time.</summary>
    Absolute,

    /// <summary>Seconds since the start of the log (or since the previous frame).</summary>
    Relative,

    /// <summary>No timestamp present.</summary>
    Missing,
}
