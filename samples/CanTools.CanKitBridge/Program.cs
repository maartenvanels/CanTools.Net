// End-to-end proof that CanTools.Net (encode/decode) and CanKit (bus transport)
// compose: encode a frame with CanTools, push it across a CanKit virtual bus,
// receive it on a second handle, and decode it back with CanTools.
//
//   dotnet run --project samples/CanTools.CanKitBridge

using System.Globalization;
using CanKit.Abstractions.API.Can.Definitions;
using CanKit.Core;
using CanTools;
using CanTools.CanKitBridge;
using CanTools.Formats.Dbc;

// Keep output identical on every machine (e.g. "0.25" rather than "0,25").
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// Same embedded database as samples/CanTools.Sample, so the two stay related.
const string dbc = """
    VERSION ""
    NS_ :
    BS_:
    BU_: PowertrainECU

    BO_ 496 EngineStatus: 8 PowertrainECU
     SG_ EngineSpeed  : 0|16@1+ (0.25,0) [0|16383.75] "rpm" Vector__XXX
     SG_ CoolantTemp  : 16|8@1+ (1,-40)  [-40|215]    "degC" Vector__XXX
     SG_ GearPosition : 24|4@1+ (1,0)    [0|6]        "" Vector__XXX

    VAL_ 496 GearPosition 0 "Neutral" 1 "First" 2 "Second" 3 "Third" 4 "Fourth" 5 "Fifth" 6 "Reverse" ;
    """;

var db = DbcReader.LoadString(dbc);
var message = db.GetMessageByName("EngineStatus");

// 1. Encode physical values into a raw payload with CanTools.Net.
byte[] payload = db.EncodeMessage("EngineStatus", new Dictionary<string, SignalValue>
{
    ["EngineSpeed"] = 2500.0,    // rpm
    ["CoolantTemp"] = 90.0,      // degC
    ["GearPosition"] = "Third",  // choice label
});
Console.WriteLine($"Encoded 0x{message.FrameId:X3}: {Convert.ToHexString(payload)}");

// 2. Open two handles on the same in-process virtual bus: one to receive, one
//    to transmit. Open the receiver first, before transmitting.
using var rxBus = CanBus.Open("virtual://alpha/0");
using var txBus = CanBus.Open("virtual://alpha/0");

// 3. Push the encoded frame across the bus via CanKit.
txBus.Transmit(FrameBridge.ToCanKit(message.FrameId, payload));

// 4. Receive it synchronously from the receiver, with a timeout. CanReceiveData
//    is a struct, so materialize the result and check the count rather than
//    comparing against a default/null sentinel.
var received = rxBus.Receive(1, 2000).ToList();
if (received.Count == 0)
{
    Console.Error.WriteLine("No frame received from the virtual bus.");
    return 1;
}

// 5. Decode the frame that came off the bus with CanTools.Net, by the id that
//    survived the round trip.
var (frameId, data) = FrameBridge.FromCanKit(received[0].CanFrame);
var decoded = db.DecodeMessage(frameId, data);

Console.WriteLine($"\nDecoded 0x{frameId:X3} off the bus:");
foreach (var (name, value) in decoded)
    Console.WriteLine($"  {name} = {value}");

// Round-trip proof: the choice label we encoded came back through the bus.
bool ok = decoded["GearPosition"].ToString() == "Third";
Console.WriteLine(ok
    ? "\nRound trip OK: GearPosition = Third survived encode -> bus -> decode."
    : "\nRound trip FAILED: GearPosition did not match.");

// 6. Prove the ICanChannel adapter composes with the rest of CanTools.Net: wrap
//    the receiving bus handle and hand it to the SDO client. We stop short of a
//    live SDO call -- that needs a CANopen server answering on the other end of
//    the bus, which this virtual demo doesn't have, so UploadAsync/DownloadAsync
//    would just block until they time out.
var channel = new CanKitCanChannel(rxBus);
var sdo = new CanTools.CanOpen.SdoClient(channel, nodeId: 0x0A);
Console.WriteLine($"\n{sdo.GetType().Name} built over a {nameof(CanKitCanChannel)} (node 0x{0x0A:X2}); " +
    "no live SDO call is made since the virtual bus has no CANopen server to answer it.");

return ok ? 0 : 2;
