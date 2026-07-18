using CanKit.Abstractions.API.Can.Definitions;

namespace CanTools.CanKitBridge;

/// <summary>
/// Bridges CanTools.Net's (frame id, payload) representation and CanKit's
/// <see cref="CanFrame"/>. Classic 11-bit data frames only. To support CAN FD
/// or extended (29-bit) ids, pass the extended flag to CanFrame.Classic (its
/// third parameter) or switch to CanFrame.Fd here, and carry the flag through
/// FromCanKit alongside frame.ID.
/// </summary>
internal static class FrameBridge
{
    public static CanFrame ToCanKit(uint frameId, byte[] payload) =>
        CanFrame.Classic((int)frameId, payload);

    public static (uint FrameId, byte[] Payload) FromCanKit(CanFrame frame) =>
        ((uint)frame.ID, frame.Data.ToArray());
}
