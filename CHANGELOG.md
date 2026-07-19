# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and from 1.0.0 onward
this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-07-17

First stable release. The public API is now covered by semantic versioning:
breaking changes to it will only ship in a new major version.

### Added

- **Standalone `cantools-net` executables on GitHub releases.** Each tagged
  release now attaches self-contained single-file builds of the CLI for
  `win-x64`, `linux-x64`, `osx-x64` and `osx-arm64`, so the tool can be run
  without installing the .NET SDK. The NuGet packages continue to publish
  alongside them.

### Fixed

- **DBC load could throw `OverflowException` on FLOAT attribute ranges.** A
  `BA_DEF_ ... FLOAT` definition using the full single-precision range in
  scientific notation (e.g. `-3.4E+038 3.4E+038`, as emitted by some vendor
  tools) was parsed through `System.Decimal`, whose range is far smaller, and
  overflowed. Float bounds and values are now parsed directly as `double`.
- **`SignalValue.ToDouble()`/`ToInt64()`/`ToUInt64()` threw on decoded choice
  values.** A value decoded through a value table is a `NamedSignalValue`, which
  carries the raw integer it maps. Numeric access now returns that raw value
  instead of throwing, so decoding with the default `decodeChoices: true` no
  longer forces a separate raw decode to read the number. A plain string choice
  label (which has no numeric backing) still throws.

## [0.2.0] - 2026-07-16

### Added

- `CanTools.Net.Cli` dotnet tool (`cantools-net`) porting the `decode`, `dump`,
  `list` and `convert` subcommands of the cantools CLI.

## [0.1.0] - 2026-07-16

Initial public release: a C#/.NET port of the Python
[cantools](https://github.com/cantools/cantools) library.

### Added

- **DBC** reader and writer (attributes, value tables, comments, signal groups,
  long-name resolution, J1939/CAN FD detection, relation attributes).
- **KCD** and **SYM 6.0** readers, with format auto-detection in
  `DatabaseLoader`.
- Message **encode/decode** with scaling, choices, floats, and simple and
  extended multiplexing, verified against the cantools test suite.
- **J1939** frame id and PGN helpers.
- **Log reading**: candump (plain, `-tz`, `-l`, `-tA`) and PCAN trace 1.0–2.1.
- **CANopen** (beyond upstream cantools): EDS/DCF object dictionaries, PDO → 
  signal projection, CiA 301 frame codecs (NMT, heartbeat, EMCY, SYNC, TIME,
  SDO), and log interpretation with SDO reassembly.

[1.0.0]: https://github.com/maartenvanels/CanTools.Net/releases/tag/v1.0.0
[0.2.0]: https://github.com/maartenvanels/CanTools.Net/releases/tag/v0.2.0
[0.1.0]: https://github.com/maartenvanels/CanTools.Net/releases/tag/v0.1.0
