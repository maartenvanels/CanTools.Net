namespace CanTools.CanOpen;

/// <summary>
/// The direction of an SDO frame. The same command bits mean different services
/// each way, so parsing needs to know which side sent the frame.
/// </summary>
public enum SdoDirection
{
    /// <summary>Client to server (COB-ID 0x600 + node id).</summary>
    Request,

    /// <summary>Server to client (COB-ID 0x580 + node id).</summary>
    Response,
}
