# CanTools.Net by example

All examples assume a project referencing `CanTools` on .NET 8 or later. The API
mirrors the Python [cantools](https://github.com/cantools/cantools) library where
that makes sense, so its documentation is often a useful second reference.

- [Loading a database](#loading-a-database)
- [Exploring messages and signals](#exploring-messages-and-signals)
- [Decoding messages](#decoding-messages)
- [Encoding messages](#encoding-messages)
- [Multiplexed messages](#multiplexed-messages)
- [Decoding a CAN log file](#decoding-a-can-log-file)
- [Writing a DBC file](#writing-a-dbc-file)
- [J1939 helpers](#j1939-helpers)
- [CANopen object dictionaries (EDS/DCF)](#canopen-object-dictionaries-edsdcf)
- [Decoding CANopen PDOs and logs](#decoding-canopen-pdos-and-logs)
- [The command line tool](#the-command-line-tool)

## Loading a database

Each supported format has a reader that produces the same `Database` model:

```csharp
using CanTools.Formats.Dbc;
using CanTools.Formats.Kcd;
using CanTools.Formats.Sym;

var db = DbcReader.LoadFile("vehicle.dbc");
var fromKcd = KcdReader.LoadFile("vehicle.kcd");
var fromSym = SymReader.LoadFile("vehicle.sym");
```

If you don't want to name the format, `DatabaseLoader` picks it from the file
extension — and when the extension is inconclusive, it probes the content
against every supported format:

```csharp
using CanTools.Formats;

var detected = DatabaseLoader.LoadFile("vehicle.kcd");
var probed = DatabaseLoader.LoadString(someDatabaseText);
```

Every reader also has a `LoadString` overload for text you already have in
memory. By default loading is *strict*: signals that overlap or fall outside
their message throw a `ParseException`. Pass `strict: false` to accept such
files anyway:

```csharp
var lenient = DbcReader.LoadFile("legacy.dbc", strict: false);
```

## Exploring messages and signals

`Database` exposes the network as messages, signals, nodes and buses:

```csharp
foreach (var message in db.Messages)
{
    Console.WriteLine($"0x{message.FrameId:X3} {message.Name} ({message.Length} bytes)");

    foreach (var signal in message.Signals)
    {
        Console.WriteLine(
            $"    {signal.Name}: {signal.Length} bits, " +
            $"scale {signal.Scale}, offset {signal.Offset} {signal.Unit}");
    }
}
```

Look up a specific message by name or frame id:

```csharp
var byName = db.GetMessageByName("EngineStatus");
var byId = db.GetMessageByFrameId(0x1F0);

// Non-throwing variants:
if (db.TryGetMessageByFrameId(0x1F0, out var found))
{
    Console.WriteLine(found.Name);
}
```

Signals carry the usual metadata: `Minimum`/`Maximum`, `Unit`, `Comment`,
`Choices` (value tables), `IsSigned`, `ByteOrder`, and so on:

```csharp
var signal = byName.GetSignalByName("GearPosition");

if (signal.Choices is { } choices)
{
    foreach (var (raw, label) in choices)
    {
        Console.WriteLine($"{raw} = {label}");
    }
}
```

## Decoding messages

`DecodeMessage` takes the raw frame payload and returns a dictionary of signal
name to `SignalValue`:

```csharp
byte[] data = [0x01, 0x45, 0x23, 0x00, 0x11, 0x00, 0x00, 0x00];

var decoded = db.DecodeMessage("EngineStatus", data);

foreach (var (name, value) in decoded)
{
    Console.WriteLine($"{name} = {value}");
}
```

`SignalValue` is a small union type: depending on the signal it holds an
integer, a floating-point number, or a named choice label. `ToString()` renders
it naturally; `ToDouble()` gives the numeric value:

```csharp
double rpm = decoded["EngineSpeed"].ToDouble();
```

By default raw values are scaled and mapped through value tables. Both can be
turned off:

```csharp
var raw = db.DecodeMessage("EngineStatus", data, decodeChoices: false, scaling: false);
```

You can also decode via the frame id, which is what you want when processing
live traffic:

```csharp
var fromBus = db.DecodeMessage(0x1F0u, data);
```

## Encoding messages

`EncodeMessage` is the inverse. `SignalValue` converts implicitly from `int`,
`long`, `double`, and `string` (for choice labels), so the dictionary reads
naturally:

```csharp
using CanTools;

var payload = db.EncodeMessage("EngineStatus", new Dictionary<string, SignalValue>
{
    ["EngineSpeed"] = 1500.5,     // scaled physical value
    ["GearPosition"] = "Neutral", // choice label
    ["CheckEngine"] = 1,
});
```

Encoding is strict by default: every signal of the message must be provided and
values must be inside the signal's minimum/maximum. Pass `strict: false` to
relax that, and `padding: true` to fill unused bits with the message's padding
pattern:

```csharp
var partial = db.EncodeMessage(
    "EngineStatus",
    new Dictionary<string, SignalValue> { ["EngineSpeed"] = 1500.5 },
    strict: false,
    padding: true);
```

## Multiplexed messages

For multiplexed messages, include the multiplexer signal in the values you
encode; only the signals active for that multiplexer id are required:

```csharp
var muxPayload = db.EncodeMessage("SensorData", new Dictionary<string, SignalValue>
{
    ["SensorSelect"] = 1,      // the multiplexer
    ["Temperature"] = 21.75,   // only valid when SensorSelect == 1
});
```

Decoding returns the multiplexer signal plus whichever signals were active in
that frame. `Message.IsMultiplexed` tells you whether a message uses
multiplexing at all, and `Message.SignalTree` exposes the multiplexer
hierarchy.

## Decoding a CAN log file

`LogParser` reads candump output (plain, `-tz`, `-ta`, `-tA`, `-l`) and PCAN
trace files (1.0–2.1), auto-detecting the format from the first parseable line.
Combine it with a database to decode a whole capture:

```csharp
using CanTools.Logs;

using var reader = new StreamReader("capture.log");
var parser = new LogParser(reader);

foreach (var entry in parser.ReadEntries())
{
    if (!db.TryGetMessageByFrameId(entry.FrameId, out var message))
    {
        continue; // frame not in the database
    }

    var signals = message.Decode(entry.Data);
    Console.WriteLine($"{entry.TimeOffset} {message.Name}: " +
        string.Join(", ", signals.Select(s => $"{s.Key}={s.Value}")));
}
```

Each `LogEntry` carries the channel, frame id, payload, remote/extended flags,
and either an absolute `Timestamp` or a relative `TimeOffset`, depending on the
log format. You can also feed single lines to `parser.Parse(line)` — it returns
`null` for lines that are not CAN frames.

## Writing a DBC file

A database can be serialized back to DBC text, whichever format it was loaded
from:

```csharp
File.WriteAllText("exported.dbc", db.ToDbcString());
```

Round-tripping preserves attributes, value tables, comments, and signal groups.
Names longer than 32 characters are handled with the standard
`SystemMessageLongSymbol` attributes, like other DBC tools do.

## J1939 helpers

The `J1939` class packs and unpacks 29-bit J1939 frame ids and PGNs:

```csharp
using CanTools;

var fields = J1939.FrameIdUnpack(0x18FEF117);
Console.WriteLine($"priority={fields.Priority} pgn=0x{J1939.PgnFromFrameId(0x18FEF117):X} " +
    $"source=0x{fields.SourceAddress:X2}");

uint frameId = J1939.FrameIdPack(
    priority: 6, reserved: 0, dataPage: 0,
    pduFormat: 0xFE, pduSpecific: 0xF1, sourceAddress: 0x17);
```

Messages loaded from a J1939 DBC report `Protocol == "j1939"`, and their
signals expose the SPN via `Signal.Spn`.

## CANopen object dictionaries (EDS/DCF)

`EdsReader` parses CiA 306 EDS and DCF files into an `ObjectDictionary`,
including `$NODEID` expressions and compact (`CompactSubObj`) arrays:

```csharp
using CanTools.Formats.Eds;

var od = EdsReader.LoadFile("device.eds", nodeId: 2);

// GetVariable returns null when the index is not in the dictionary.
if (od.GetVariable(0x1017) is { } heartbeat) // Producer heartbeat time
{
    Console.WriteLine($"{heartbeat.Name}: {heartbeat.DataType}, default {heartbeat.Default}");
}

if (od.TryGetEntry(0x1018, out var identity))
{
    Console.WriteLine(identity.Name); // "Identity object"
}
```

Entries are variables, records, or arrays; records and arrays expose their
members through `TryGetMember(subindex, out var member)`.

## Decoding CANopen PDOs and logs

`PdoDatabase.Create` reads the PDO communication and mapping parameters from an
object dictionary and projects them onto a plain `Database`, so PDO frames
decode with the same tooling as any other message:

```csharp
using CanTools.CanOpen;

var od = EdsReader.LoadFile("motor.eds", nodeId: 5);
var pdos = PdoDatabase.Create(od);

var values = pdos.DecodeMessage(0x185u, frame);   // TPDO1 of node 5
```

For the protocol frames there are stateless codecs — `NmtMessage`,
`HeartbeatMessage`, `EmergencyMessage`, `SyncMessage`, `TimeMessage` and the
`SdoFrame` family — each with `Parse` and `ToBytes`:

```csharp
var emergency = EmergencyMessage.Parse(frame);
Console.WriteLine(emergency);   // Code 0x2001, Current
```

`CanOpenLogInterpreter` combines all of it: it folds a log stream into typed
events, reassembling SDO transfers (expedited, segmented and block) along the
way. Pattern match on the event types you care about:

```csharp
using var reader = new StreamReader("capture.log");
var interpreter = new CanOpenLogInterpreter(pdos);

foreach (var evt in interpreter.Interpret(new LogParser(reader).ReadEntries()))
{
    switch (evt)
    {
        case PdoEvent pdo:
            Console.WriteLine($"{pdo.Message.Name}: {pdo.Signals["Velocity"]}");
            break;
        case EmergencyEvent emcy:
            Console.WriteLine($"node {emcy.NodeId}: {emcy.Emergency}");
            break;
        case SdoDownloadEvent sdo:
            Console.WriteLine($"wrote 0x{sdo.Index:X4}:{sdo.Subindex} = {Convert.ToHexString(sdo.Data)}");
            break;
        case HeartbeatEvent { IsStateChange: true } hb:
            Console.WriteLine($"node {hb.NodeId} -> {hb.Heartbeat.State}");
            break;
    }
}
```

See [CANOPEN.md](../CANOPEN.md) for the design notes and the exact upstream
(python-canopen) semantics that are matched.

## The command line tool

The `CanTools.Net.Cli` package wraps the library as the `cantools-net` command,
a port of the cantools CLI:

```
dotnet tool install --global CanTools.Net.Cli
```

`decode` reads candump or PCAN log lines from stdin and appends the decoded
signals to every known frame — pipe a live bus or an existing capture into it:

```
$ candump can0 | cantools-net decode vehicle.dbc
  can0  1F0   [8]  C0 06 E0 00 00 00 00 00 ::
ExampleMessage(
    Enable: Enabled,
    AverageRadius: 3.2 m,
    Temperature: 250.55 degK
)
```

Use `--single-line` for one frame per line (easier to grep), `-c` to keep raw
values instead of choice labels, and `-m <mask>` to match frame ids under a mask
(e.g. J1939 captures where the source address varies).

`list` prints message names, or full details with `--all` or a message name;
`-b` and `-c` list buses and nodes instead:

```
$ cantools-net list vehicle.dbc
ExampleMessage
$ cantools-net list vehicle.dbc ExampleMessage
ExampleMessage:
  Comment[None]: Example message used as template in MotoHawk models.
  ...
```

`dump` renders every message with its bit layout, signal tree and choices
(`-m <name>` for a single message, `--with-comments` to include signal
comments):

```
$ cantools-net dump vehicle.dbc
```

`convert` reads any supported format and writes a DBC (KCD/SYM output follows
once those writers are ported):

```
$ cantools-net convert vehicle.kcd vehicle.dbc
```

All subcommands accept `--prune` to shorten named choice values and
`--no-strict` to skip the database consistency checks; `decode`, `dump` and
`convert` also take `-e <encoding>` for non-default file encodings.
