namespace CanTools.CanOpen;

/// <summary>Tuning for <see cref="SdoClient"/>.</summary>
public sealed class SdoClientOptions
{
    /// <summary>How long to wait for each response frame before timing out.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Whether to attempt block transfer for large values. Falls back to segmented
    /// transfer when the server aborts the block initiate.
    /// </summary>
    public bool EnableBlockTransfer { get; init; }

    /// <summary>Segments per block requested from the server (1..127).</summary>
    public int BlockSize { get; init; } = 127;
}
