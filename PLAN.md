# CanTools.Net — porting plan

A C#/.NET port of the [cantools](https://github.com/cantools/cantools) Python library:
parse CAN database files (DBC first, later SYM/KCD), encode/decode CAN messages, and
read CAN log data. MIT licensed, with full attribution to the original project.

Working name: **CanTools.Net** (namespace `CanTools`). Rename later if we want more
distance from the upstream project; the README must state it is an independent port,
not affiliated with the cantools maintainers.

## Why

Researched July 2026:

- There is **no existing cantools port** for .NET.
- The closest library is [DbcParserLib](https://github.com/EFeru/DbcParser) (MIT, active,
  ~85k downloads). It covers DBC parsing and basic pack/unpack, but lacks extended
  multiplexing (`SG_MUL_VAL_`), signal groups, J1939 helpers, DBC writing, and any
  non-DBC format. [EasyDbc](https://github.com/Vico-wu/EasyDbc) adds DBC writing on top
  but is a one-maintainer extension.
- KCD, SYM, ARXML comm-matrix and CDD parsing: **nothing exists in .NET**.
- Managed BLF/ASC log readers: **nothing exists in .NET** (separate gap, out of scope
  for the first milestones but a natural follow-up).

So a faithful port of cantools' database layer fills a real hole.

## Licensing and attribution

- cantools is MIT (Copyright (c) 2015-2019 Erik Moqvist). A derivative port is allowed.
- This project is MIT as well. `THIRD-PARTY-NOTICES.txt` carries the original cantools
  license text; the README links to the upstream project prominently.
- The test data files under `tests/files/` are copied from the cantools repo (also MIT).

## Architecture

```
src/CanTools/                the library (single project to start)
  Packing/                   bit-level pack/unpack engine (replaces Python bitstruct)
  Model/                     Signal, Message, Node, Bus, attributes, value tables
  Formats/Dbc/               DBC reader + writer
  Formats/Sym/, Formats/Kcd/ later phases
  Logs/                      candump/log text parsing (later phase)
tests/CanTools.Tests/        xUnit tests, ported from cantools' test_database.py
tests/files/                 golden test data copied from cantools (83 dbc, 21 sym, 8 kcd, ...)
reference/cantools/          shallow clone of upstream, for reference only (gitignored)
```

Design rules (this is a port of behavior, not of Python code):

- Idiomatic C#: `Span<byte>` for packing, real types instead of dicts, exceptions
  instead of error tuples. Do not transliterate Python line by line.
- Decoded values are heterogeneous in cantools (float | int | string label). Model this
  explicitly with a small `SignalValue` type instead of `Dictionary<string, object>`.
- The parser is hand-written (tokenizer + recursive descent). cantools uses the
  `textparser` grammar library; we do not port that, we only match its accepted input.
- DBC files are typically cp1252, not UTF-8. Encoding is a constructor option like
  upstream.
- Target `net8.0` initially; consider `netstandard2.0` multi-targeting before a NuGet
  release, not before. Decision (July 2026, at packaging time): stay `net8.0`-only.
  The code leans on `GeneratedRegex`, `Span<byte>` and modern C# throughout, so
  `netstandard2.0` would mean polyfills and a second code path for little gain;
  revisit only if a concrete consumer needs .NET Framework.

## Phases

Each phase is an isolated, independently testable unit of work. One phase (or one
module within a phase) per session/subagent, following the `port-module` skill.
Status is tracked in `PORTING.md`.

**Phase 0 — scaffold** ✔ set up by this plan
Solution, projects, CI-ready test run, LICENSE/NOTICES, test data copied.

**Phase 1 — packing engine** (`Packing/`)
Port the encode/decode core of `database/utils.py` + `conversion.py`: pack/unpack of
signed/unsigned/float fields at arbitrary bit positions, both byte orders, scaling with
the same numeric behavior as upstream (integer fast path vs double). cantools' trick is
packing big-endian and little-endian signals in two passes and OR-ing the results;
in C# implement direct bit manipulation but verify against the same expectations.
Tests: hand-ported round-trip cases from `test_database.py` (encode/decode sections).
This phase has zero parsing — messages are built in code.

**Phase 2 — data model + multiplexing** (`Model/`)
`Signal`, `Message`, `Node`, `Bus`, attributes, choices/value tables, signal groups,
`Message.Encode/Decode` including simple and extended multiplexing and the recursive
signal tree, strict-mode overlap/size validation.
Tests: multiplexing and validation cases from `test_database.py`.

**Phase 3 — DBC reader** (`Formats/Dbc/`)
Port `dbc.py` parsing: all sections (`BO_`, `SG_`, `VAL_`, `BA_*`, `CM_`, `SIG_GROUP_`,
`SG_MUL_VAL_`, ...), quoted-string escapes, multi-line statements, encoding option.
Tests: golden-file tests over the 83 `.dbc` files — this is the bulk of the ported
test suite and the acceptance gate for the whole project.

**Phase 4 — DBC writer**
Port `dump_file`: write a database back to DBC, faithful enough to pass upstream's
round-trip/dump tests.

**Phase 5 — SYM + KCD readers, J1939 helpers**
`sym.py` (862 loc), `kcd.py` (390 loc), `j1939.py` (small). Same golden-file approach.

**Phase 6 — log reading** (`Logs/`)
Port `logreader.py` (candump text formats) + `test_logreader.py`. This is what makes
the library useful for "log and interpret a bus" together with any vendor interface
(PCAN, Kvaser, SocketCANSharp).

**Phase 7 — CLI** ✔ (July 2026, added after the original plan)
The `decode`, `dump`, `list` and `convert` subcommands as a separate
`src/CanTools.Cli/` project, packaged as the dotnet tool `cantools-net`
(`CanTools.Net.Cli`). See PORTING.md for scope cuts and deviations.

**Later / explicitly out of scope for now**
ARXML (~2000 loc, plan as its own project when needed), CDD/diagnostics, tester,
monitor TUI, plotting, C code generation, a python-can-style hardware abstraction.

**Beyond upstream: CANopen (proposed)**
EDS/DCF reading, PDO-mapping → signal projection onto the existing Database, and
stateless CiA 301 frame codecs — see `CANOPEN.md` for the full design. This is an
extension, not part of the cantools port.

## Test strategy

- `test_database.py` (5.6k lines) is data-driven: load file, assert model shape, assert
  encode/decode bytes. Port it incrementally per phase into xUnit; keep the upstream
  test method names in a comment or test name so coverage is auditable.
- Differential testing: the `diff-test` skill runs Python cantools from
  `reference/cantools` against the C# build on the same input and compares output.
  Use it whenever a golden expectation is unclear or a test was hard to port.
- A phase is done when: its ported tests pass, `dotnet test` is green, and `PORTING.md`
  is updated with what was ported and what was intentionally skipped.
