namespace CanTools.CanOpen;

/// <summary>The [DeviceInfo] section of an EDS file. All fields are optional.</summary>
public sealed class DeviceInformation
{
    /// <summary>The supported baudrates in bit/s.</summary>
    public IReadOnlySet<int> AllowedBaudrates { get; internal set; } = new HashSet<int>();

    public string? VendorName { get; internal set; }

    public long? VendorNumber { get; internal set; }

    public string? ProductName { get; internal set; }

    public long? ProductNumber { get; internal set; }

    public long? RevisionNumber { get; internal set; }

    public string? OrderCode { get; internal set; }

    public bool? SimpleBootUpMaster { get; internal set; }

    public bool? SimpleBootUpSlave { get; internal set; }

    public bool? Granularity { get; internal set; }

    public bool? DynamicChannelsSupported { get; internal set; }

    public bool? GroupMessaging { get; internal set; }

    public int? RpdoCount { get; internal set; }

    public int? TpdoCount { get; internal set; }

    public bool? LssSupported { get; internal set; }
}
