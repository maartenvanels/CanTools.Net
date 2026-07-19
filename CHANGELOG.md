# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and from 1.0.0 onward
this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **Dictionary-driven SDO reads and writes.** `SdoClient.UploadAsync` /
  `DownloadAsync` overloads that take an `ObjectDictionary` (loaded from an
  EDS/DCF) and an entry — by name, by index, or as an `OdVariable` — resolve the
  index, subindex and CiA 301 data type from the dictionary, so calling code no
  longer repeats raw indices and type codes. See `CANOPEN.md`.
- **Standard SDO commands.** `SdoClient.StoreParametersAsync` (0x1010, "save") and
  `RestoreDefaultParametersAsync` (0x1011, "load"), each addressing a
  `CanOpenParameterGroup` (all/communication/application).
- **`CanOpenObjects`** — named constants for the standard CiA 301
  communication-profile object indices (`DeviceType`, `ManufacturerDeviceName`,
  `StoreParameters`, `IdentityObject`, …).
- **DCF writer (`DcfWriter`).** Writes an `ObjectDictionary` loaded from an EDS/DCF
  back to a DCF, preserving the source file's comments, ordering and unknown keys and
  layering in the commissioning data and configured `ParameterValue`s. An unchanged
  dictionary round-trips byte-for-byte; untouched values (including `$NODEID`
  expressions) are echoed verbatim. New `ObjectDictionary.SetValue` and public
  `NodeId`/`Bitrate` setters drive it. See `CANOPEN.md`.

### Changed

- **`samples/CanTools.CanOpenSample`** now loads its object dictionary from a
  bundled `VirtualNode.eds`, seeds the simulated node from the EDS defaults, and
  reads/writes/programs it by parameter name. Pass a path to a real vendor EDS to
  list and read its parameters (`dotnet run --project
  samples/CanTools.CanOpenSample -- path/to/device.eds`).

## [1.1.0] - 2026-07-19

### Added

- **Live CANopen SDO client (`SdoClient`).** Reads (upload) and writes (download)
  a remote node's object dictionary over the new dependency-free `ICanChannel`
  transport abstraction, handling expedited, segmented and block transfer
  transparently, with automatic fallback from block to segmented and a typed
  value helper (`SdoValueCodec`, bytes ↔ `OdValue` by CiA 301 data type).
  Responses are correlated by index and subindex. Behavior is verified against
  SDO test vectors learned from (not copied out of) the Apache-2.0
  [lely-core](https://gitlab.com/lely_industries/lely-core) stack; see
  `THIRD-PARTY-NOTICES.txt`.
- **`CanTools.Transport` namespace.** `CanFrame` and `ICanChannel` — a minimal,
  dependency-free seam between the codec layer and a CAN bus. Bring your own
  channel; the core ships none, so it stays dependency-free.
- **Samples.** `samples/CanTools.CanOpenSample` runs the SDO client end to end
  against an in-process simulated node (no hardware), and the CanKit bridge
  sample gains a `CanKitCanChannel` `ICanChannel` adapter.
- **Optional lely-core loopback interop test tier** (Linux/vcan, opt-in via
  `CANTOOLS_INTEROP=1`); skipped in the default cross-platform build.

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
