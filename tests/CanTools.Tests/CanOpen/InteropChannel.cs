using CanTools.Transport;

namespace CanTools.Tests.CanOpen;

/// <summary>
/// Test-only wiring for the optional lely loopback interop tier (see
/// docs/interop/lely-loopback.md). This library ships no SocketCAN/vcan adapter —
/// vcan is Linux-only and out of scope for the cross-platform core — so
/// <see cref="OpenVcan0"/> is a placeholder that always throws a
/// <see cref="Xunit.SkipException"/> pointing at the guide. Anyone who sets
/// CANTOOLS_INTEROP=1 without also replacing this method with a real
/// <see cref="ICanChannel"/> binding (e.g. a SocketCAN wrapper, or the CanKit
/// adapter running on Linux) gets a clear SKIP with instructions, never a
/// confusing crash.
/// </summary>
public static class InteropChannel
{
    /// <summary>Node id used by the lely slave/coctl instance in the interop guide.</summary>
    public const byte LelyNodeId = 0x0A;

    /// <summary>
    /// Intended to open a channel bound to the vcan0 interface set up per
    /// docs/interop/lely-loopback.md. Always throws until someone running the
    /// interop tier on Linux swaps in a real ICanChannel implementation.
    /// </summary>
    public static ICanChannel OpenVcan0()
    {
        throw new Xunit.SkipException(
            "InteropChannel.OpenVcan0 is a placeholder: this repo bundles no SocketCAN/vcan " +
            "adapter (vcan is Linux-only). To run the interop tier, wire a real ICanChannel to " +
            "vcan0 (a SocketCAN binding, or the CanKit adapter on Linux) per " +
            "docs/interop/lely-loopback.md, then replace this method's body accordingly.");
    }
}
