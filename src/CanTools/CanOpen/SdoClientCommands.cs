namespace CanTools.CanOpen;

/// <summary>
/// The standard CiA 301 SDO commands built on top of an ordinary download: saving
/// parameters to non-volatile memory (0x1010) and restoring the defaults (0x1011).
/// Each is triggered by writing a fixed four-byte signature to the requested group's
/// subindex.
/// </summary>
public static class SdoClientCommandExtensions
{
    // The signatures are the ISO 8859 characters, sent as the four data bytes: a
    // little-endian UNSIGNED32 of 0x65766173 ("save") and 0x64616F6C ("load").
    private static readonly byte[] SaveSignature = "save"u8.ToArray();
    private static readonly byte[] LoadSignature = "load"u8.ToArray();

    /// <summary>
    /// Stores the given parameter <paramref name="group"/> to non-volatile memory by
    /// writing "save" to 0x1010, so the values survive a reset.
    /// </summary>
    public static Task StoreParametersAsync(
        this SdoClient client, CanOpenParameterGroup group = CanOpenParameterGroup.All,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.DownloadAsync(
            CanOpenObjects.StoreParameters, (byte)group, SaveSignature.AsSpan().ToArray(),
            cancellationToken);
    }

    /// <summary>
    /// Restores the given parameter <paramref name="group"/> to its defaults by writing
    /// "load" to 0x1011. On most devices the defaults take effect after the next reset.
    /// </summary>
    public static Task RestoreDefaultParametersAsync(
        this SdoClient client, CanOpenParameterGroup group = CanOpenParameterGroup.All,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        return client.DownloadAsync(
            CanOpenObjects.RestoreDefaultParameters, (byte)group, LoadSignature.AsSpan().ToArray(),
            cancellationToken);
    }
}
