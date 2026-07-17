# CanTools.Net

[![CI](https://github.com/maartenvanels/CanTools.Net/actions/workflows/ci.yml/badge.svg)](https://github.com/maartenvanels/CanTools.Net/actions/workflows/ci.yml)

A C#/.NET port of the excellent [cantools](https://github.com/cantools/cantools)
Python library by Erik Moqvist and contributors.

What works today:

- **DBC**: read and write, including attributes, value tables, comments, signal
  groups, long-name resolution, J1939/CAN FD detection and relation attributes.
- **KCD** and **SYM 6.0**: read. `DatabaseLoader.LoadFile` picks the format from
  the file extension, or probes the content when the extension is unknown.
- **Encode/decode** CAN messages with scaling, choices, floats, and simple and
  extended multiplexing — verified against the cantools test suite.
- **J1939** frame id and PGN helpers.
- **Log reading**: candump (plain, `-tz`, `-l`, `-tA`) and PCAN trace 1.0–2.1.
- **CANopen**: read EDS/DCF object dictionaries (CiA 306, `$NODEID` expressions,
  compact arrays — verified against [python-canopen](https://github.com/christiansandberg/canopen)),
  project PDO mappings onto ordinary decodable messages, parse and build the
  CiA 301 frames (NMT, heartbeat, EMCY, SYNC, TIME, SDO), and fold a candump of a
  CANopen network into typed events with SDO reassembly — segmented and block
  transfers included. This goes beyond upstream cantools; see `CANOPEN.md`.

```csharp
using CanTools.Formats.Dbc;

var db = DbcReader.LoadFile("vehicle.dbc");
var data = db.EncodeMessage("ExampleMessage", new Dictionary<string, SignalValue>
{
    ["Temperature"] = 250.55,
    ["Enable"] = "Enabled",
});
var decoded = db.DecodeMessage("ExampleMessage", data);
```

More examples — loading databases, decoding log files, J1939, CANopen — in
[docs/examples.md](docs/examples.md).

## Command line

The `CanTools.Net.Cli` package ports the cantools command line (the `decode`,
`dump`, `list` and `convert` subcommands):

```
dotnet tool install --global CanTools.Net.Cli

candump can0 | cantools-net decode vehicle.dbc
cantools-net dump vehicle.dbc
cantools-net list vehicle.dbc
cantools-net convert vehicle.kcd vehicle.dbc
```

If you would rather not install the .NET SDK, each
[GitHub release](https://github.com/maartenvanels/CanTools.Net/releases) also
attaches a self-contained `cantools-net` executable for Windows, Linux and macOS
(x64 and Apple Silicon). Download the one for your platform, and run it directly —
no runtime required (on Linux/macOS, `chmod +x` it first).

This is an independent port and is not affiliated with the cantools maintainers.
CANopen is a registered trademark of CAN in Automation (CiA); this project is not
affiliated with or endorsed by CiA.
Behavior is verified against the upstream test suite and cross-checked against
the Python implementation; see `PORTING.md` for exactly what has been ported and
which deviations exist, and `PLAN.md` for the roadmap. Not yet ported: ARXML and
CDD parsing, KCD/SYM writing, container messages, and the `monitor`, `plot` and
`generate_c_source` subcommands.

Work in progress — the API is not stable yet.

## License

MIT. Portions derived from cantools, Copyright (c) 2015-2019 Erik Moqvist, also MIT —
see `THIRD-PARTY-NOTICES.txt`.
