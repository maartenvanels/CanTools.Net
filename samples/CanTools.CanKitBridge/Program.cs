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
//    to transmit. Open the receiver first and subscribe before transmitting.
using var rxBus = CanBus.Open("virtual://alpha/0");
using var txBus = CanBus.Open("virtual://alpha/0");

CanFrame? receivedFrame = null;
using var gotFrame = new ManualResetEventSlim(false);
// FrameReceived hands back a CanReceiveData whose .CanFrame is a real CanFrame,
// exactly what FrameBridge.FromCanKit consumes. Its successor FrameObserved
// only exposes a read-only CanFrameView, so we deliberately keep FrameReceived.
#pragma warning disable CS0618 // FrameReceived is marked obsolete in CanKit 0.5.5
rxBus.FrameReceived += (_, e) =>
{
    receivedFrame = e.CanFrame;
    gotFrame.Set();
};
#pragma warning restore CS0618

// 3. Push the encoded frame across the bus via CanKit.
txBus.Transmit(FrameBridge.ToCanKit(message.FrameId, payload));

// 4. Wait for it to arrive on the receiver.
if (!gotFrame.Wait(TimeSpan.FromSeconds(2)) || receivedFrame is null)
{
    Console.Error.WriteLine("No frame received from the virtual bus.");
    return 1;
}

// 5. Decode the frame that came off the bus with CanTools.Net, by the id that
//    survived the round trip.
var (frameId, data) = FrameBridge.FromCanKit(receivedFrame.Value);
var decoded = db.DecodeMessage(frameId, data);

Console.WriteLine($"\nDecoded 0x{frameId:X3} off the bus:");
foreach (var (name, value) in decoded)
    Console.WriteLine($"  {name} = {value}");

// Round-trip proof: the choice label we encoded came back through the bus.
bool ok = decoded["GearPosition"].ToString() == "Third";
Console.WriteLine(ok
    ? "\nRound trip OK: GearPosition = Third survived encode -> bus -> decode."
    : "\nRound trip FAILED: GearPosition did not match.");
return ok ? 0 : 2;
