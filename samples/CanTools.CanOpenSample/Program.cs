// End-to-end tour of the CanTools.Net live SDO client (CANopen).
//
// A real SDO exchange needs a CANopen node on the bus to answer. To keep this
// sample self-contained — no hardware, no external server — it talks to a small
// in-process SimulatedSdoNode that implements ICanChannel and serves a fixed
// object dictionary. On a real bus you would swap SimulatedSdoNode for a channel
// bound to your CAN interface (e.g. the CanKitCanChannel adapter in
// samples/CanTools.CanKitBridge), and the SdoClient code below is unchanged.
//
//   dotnet run --project samples/CanTools.CanOpenSample

using System.Globalization;
using System.Text;
using CanTools.CanOpen;
using CanTools.CanOpenSample;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const byte nodeId = 0x0A;

// The node's object dictionary: a couple of standard entries plus one writable
// parameter. Values are the raw little-endian bytes an SDO transfer moves.
var objectDictionary = new Dictionary<(ushort Index, byte Subindex), byte[]>
{
    // 0x1000 Device Type (UNSIGNED32) — 4 bytes, read via an expedited transfer.
    [(0x1000, 0)] = BitConverter.GetBytes(0x000F0191u),

    // 0x1008 Manufacturer Device Name (VISIBLE_STRING) — longer than 4 bytes, so
    // it is read via a segmented transfer, reassembled by the client.
    [(0x1008, 0)] = Encoding.ASCII.GetBytes("CanTools.Net Virtual Node"),

    // 0x2000 a writable UNSIGNED16 parameter, starting at 0.
    [(0x2000, 0)] = [0x00, 0x00],
};

var node = new SimulatedSdoNode(nodeId, objectDictionary);
var client = new SdoClient(node, nodeId);

Console.WriteLine($"Talking SDO to simulated node 0x{nodeId:X2}:\n");

// 1. Expedited read: Device Type (fits in four bytes).
var deviceType = await client.UploadAsync(0x1000, 0, CanOpenDataType.Unsigned32);
Console.WriteLine($"  read  0x1000/0 Device Type   = 0x{deviceType.ToUInt64():X8}");

// 2. Segmented read: Device Name (a 25-byte string, streamed in 7-byte segments).
var deviceName = await client.UploadAsync(0x1008, 0, CanOpenDataType.VisibleString);
Console.WriteLine($"  read  0x1008/0 Device Name   = \"{deviceName.Text}\"");

// 3. Write then read back a parameter, using the typed helper both ways.
await client.DownloadAsync(0x2000, 0, (OdValue)1234UL, CanOpenDataType.Unsigned16);
var readBack = await client.UploadAsync(0x2000, 0, CanOpenDataType.Unsigned16);
Console.WriteLine($"  write 0x2000/0 = 1234, read back = {readBack.ToUInt64()}");

// 4. Reading a missing entry surfaces the server's abort as a typed exception.
try
{
    await client.UploadAsync(0x1234, 0);
}
catch (SdoAbortException ex)
{
    Console.WriteLine($"  read  0x1234/0 -> aborted: {ex.Code} (0x{(uint)ex.Code:X8})");
}

var ok = deviceType.ToUInt64() == 0x000F0191
         && deviceName.Text == "CanTools.Net Virtual Node"
         && readBack.ToUInt64() == 1234;

Console.WriteLine(ok
    ? "\nExpedited + segmented + typed SDO round trip over a simulated node: OK"
    : "\nRound trip FAILED.");

return ok ? 0 : 1;
