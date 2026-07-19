using CanTools.CanOpen;

namespace CanTools.Tests.CanOpen;

// Optional interop: our SdoClient against a real lely SDO server over vcan.
// Skipped unless CANTOOLS_INTEROP=1 and a lely server is reachable on vcan0.
// This tier is Linux-only and never part of the default `dotnet test` gate —
// see docs/interop/lely-loopback.md for the full setup.
public class SdoClientInteropTests
{
    [SkippableFact]
    public async Task It_reads_the_device_type_from_a_lely_server()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("CANTOOLS_INTEROP") == "1",
            "Set CANTOOLS_INTEROP=1 with a lely server on vcan0 to run interop tests.");

        // Arrange: open the channel to vcan0 (adapter chosen by the interop guide),
        // point at the lely slave's node id, then read the mandatory 0x1000 Device Type.
        // The exact channel construction is documented in docs/interop/lely-loopback.md.
        var channel = InteropChannel.OpenVcan0();
        var client = new SdoClient(channel, InteropChannel.LelyNodeId);

        var deviceType = await client.UploadAsync(0x1000, 0, CanOpenDataType.Unsigned32);

        Assert.True(deviceType.ToUInt64() != 0, "Device Type 0x1000 must be non-zero.");
    }
}
