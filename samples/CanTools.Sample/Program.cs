// A self-contained tour of CanTools.Net: load a database, explore it, then
// encode and decode a frame. The database is embedded as a string so this runs
// with no external files — in real code you would use DbcReader.LoadFile(path).
//
//   dotnet run --project samples/CanTools.Sample

using System.Globalization;
using CanTools;
using CanTools.Formats.Dbc;

// Keep the output identical on every machine (e.g. "0.25" rather than "0,25").
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

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

Console.WriteLine("Messages in the database:");
foreach (var message in db.Messages)
{
    Console.WriteLine($"  0x{message.FrameId:X3} {message.Name} ({message.Length} bytes)");
    foreach (var signal in message.Signals)
    {
        Console.WriteLine(
            $"    {signal.Name}: {signal.Length} bits, scale {signal.Scale}, " +
            $"offset {signal.Offset} {signal.Unit}".TrimEnd());
    }
}

// Encode physical values into a frame. A choice signal takes its label directly;
// SignalValue converts implicitly from double, int and string.
var frame = db.EncodeMessage("EngineStatus", new Dictionary<string, SignalValue>
{
    ["EngineSpeed"] = 2500.0,    // rpm (scaled)
    ["CoolantTemp"] = 90.0,      // degC (offset -40)
    ["GearPosition"] = "Third",  // choice label
});

Console.WriteLine($"\nEncoded frame: {Convert.ToHexString(frame)}");

// Decode it back. GearPosition comes back as a named choice value that still
// exposes its raw number via ToDouble().
var decoded = db.DecodeMessage("EngineStatus", frame);

Console.WriteLine("\nDecoded signals:");
foreach (var (name, value) in decoded)
{
    Console.WriteLine($"  {name} = {value}");
}

var gear = decoded["GearPosition"];
Console.WriteLine($"\nGearPosition label \"{gear}\" maps to raw value {gear.ToDouble()}.");
