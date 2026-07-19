namespace CanTools.CanOpen;

/// <summary>
/// The indices of the standard CiA 301 communication-profile objects (0x1000 range),
/// so calling code can name them instead of spelling out raw indices.
/// </summary>
public static class CanOpenObjects
{
    public const ushort DeviceType = 0x1000;
    public const ushort ErrorRegister = 0x1001;
    public const ushort ManufacturerStatusRegister = 0x1002;
    public const ushort PredefinedErrorField = 0x1003;
    public const ushort CobIdSync = 0x1005;
    public const ushort CommunicationCyclePeriod = 0x1006;
    public const ushort ManufacturerDeviceName = 0x1008;
    public const ushort ManufacturerHardwareVersion = 0x1009;
    public const ushort ManufacturerSoftwareVersion = 0x100A;
    public const ushort StoreParameters = 0x1010;
    public const ushort RestoreDefaultParameters = 0x1011;
    public const ushort CobIdTime = 0x1012;
    public const ushort CobIdEmergency = 0x1014;
    public const ushort ConsumerHeartbeatTime = 0x1016;
    public const ushort ProducerHeartbeatTime = 0x1017;
    public const ushort IdentityObject = 0x1018;
}

/// <summary>
/// The parameter groups a store (0x1010) or restore (0x1011) command addresses via
/// its subindex. Higher subindices are manufacturer-specific.
/// </summary>
public enum CanOpenParameterGroup : byte
{
    /// <summary>All parameters (subindex 1).</summary>
    All = 1,

    /// <summary>Communication parameters, the 0x1000–0x1FFF range (subindex 2).</summary>
    Communication = 2,

    /// <summary>Application parameters, the 0x6000–0x9FFF range (subindex 3).</summary>
    Application = 3,
}
