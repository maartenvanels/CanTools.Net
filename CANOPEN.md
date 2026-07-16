# CANopen support — design proposal

Status: **E1–E4 done** (July 2026). Remaining: the "later" items (DCF writing,
CiA 311 XDD/XDC, CPJ).

## Progress

- **E1 — EDS/DCF reader + ObjectDictionary: done.** `Formats/Eds/EdsReader`
  (INI parser matching RawConfigParser semantics, Python int(s,0) numbers,
  $NODEID expressions, the misspelled [DeviceComissioning], CompactSubObj with
  lazy member synthesis, [xxxxName] renames, dummies, DataType>0x1B indirection,
  signed two's-complement limits) and the `CanTools.CanOpen` model
  (`ObjectDictionary`, `OdVariable`/`OdArray`/`OdRecord`, `OdValue`,
  `CanOpenDataType`, `DeviceInformation`). Tests ported from python-canopen's
  test_eds.py (43 tests); full model bulk-diffed against python-canopen for
  sample.eds (with and without node id) and datatypes.eds — zero differences
  (`EdsBulkDiffTests`, gated on CANOPEN_OD_DUMP). Deviations: invalid values are
  silently skipped (upstream logs warnings); [FileInfo] is not retained (needed
  only for the future writer); dotted string lookup ("A.B") is not implemented;
  values beyond 64 bits are rejected. Test data from python-canopen (MIT), see
  THIRD-PARTY-NOTICES.txt.
- **E2 — CobId + PdoDatabase projection: done.** `CanOpen/CobId` (function code /
  node id split, CiA 301 connection-set classification, compose) and
  `CanOpen/PdoDatabase.Create(od)` → a plain `Database`: comm records
  0x1400/0x1800 (ParameterValue beats DefaultValue, bit 31 disables — disabled
  PDOs are omitted from the Database, bit 30/29 dropped), mapping records
  0x1600/0x1A00 (`index<<16|sub<<8|bits`, size masked to 7 bits, zero and
  unresolvable entries skipped without advancing the offset, dummies map as
  ordinary variables, record/array members get dotted "Parent.Name" names),
  contiguous little-endian offsets, identity conversions (EDS has no scaling).
  Tests replicate python-canopen's test_pdo_map_bit_mapping byte-exactly through
  the projection (fixture tests/files/eds/pdo_test.eds, hand-written) plus the
  empty-PDO behavior of sample.eds; verified against python-canopen itself
  running on the same fixture (offsets, names, enabled flags identical).
  Deviation: message names use upstream's TxPDO{n}_node{id} formula, which is
  only meaningful for connection-set COB-IDs.
- **E3 — frame codecs: done.** Stateless parse/build per frame type in
  `CanOpen/`: `NmtMessage` + `NmtCommand`/`NmtState` (with the command → target
  state map), `Heartbeat` (node guarding toggle bit split off, boot-up = state
  0), `EmergencyMessage` (CiA 301 error-class table and python-canopen's
  "Code 0x2001, Current" formatting), `SyncMessage`, `TimeMessage` (1984 epoch,
  ms-of-day + days, both directions), and the `SdoFrame` record family:
  initiate upload/download requests/responses with the full e/s/n expedited
  and announced-size handling, segment frames (toggle/length/last), `SdoAbort`
  with the complete `SdoAbortCode` table, and classification-only
  `SdoBlockFrame`. Golden vectors ported from python-canopen's test_nmt/
  test_emcy/test_sync/test_time/test_sdo; the EMCY and abort description
  tables additionally diff-tested string-for-string against python-canopen
  (43 rows, zero differences). Deviations: builders throw (EncodeException/
  ArgumentException) where upstream silently truncates (EMCY vendor data,
  oversized SDO payloads); SYNC/TIME get parsers upstream lacks (produce-only
  there); block transfer frames are classified but not decoded — mid-block
  data frames carry no command specifier at all, so per-frame classification
  is impossible and reassembly waits for the E4 interpreter. Toggle-bit
  alternation checking is likewise E4 state.
- **E4 — log-stream interpreter: done.** `CanOpen/CanOpenLogInterpreter` folds
  `LogEntry` streams (any `LogParser` format) into a `CanOpenEvent` record
  family: NMT commands, boot-ups, heartbeats (with per-node state tracking and
  `IsStateChange`), emergencies, SYNC/TIME, PDOs decoded through a projected
  `Database`, and completed SDO transfers. SDO reassembly covers expedited,
  segmented and block transfers per node — block data frames are intercepted
  by phase (they carry a sequence number, not a command specifier), buffered
  per round and only committed on the receiver's ack, which makes seqno gaps
  and retransmitted rounds come out right; the end frame's n field trims the
  padding. Events fire on the server frame that completes the service (upload
  data / download ack). SDO conversations in the tests are the golden ones
  from python-canopen's test_sdo.py, including the block download/upload
  transcripts and a retransmission scenario. Deviations: block CRCs are not
  verified (a log records what happened either way), toggle-bit alternation
  is not enforced, and malformed protocol frames are skipped like unparseable
  log lines. There is no upstream oracle for the interpreter itself —
  python-canopen has no log reader — so this is original surface, anchored to
  upstream through the frame-level golden conversations.

This would be the repo's first deliberate step *beyond* upstream cantools, which has
no CANopen support. The J1939 helpers set the precedent for protocol helpers; the
mission stays the same: **files and interpretation, no bus I/O**. README and
PORTING.md must mark this as an extension, not a port.

## Why

Research (July 2026) shows the .NET gap is real:

- EDS/DCF parsing: only [eds-dcf-net](https://github.com/dborgards/eds-dcf-net)
  (MIT, brand new, file round-tripping focus, no signal projection) and the GPL-3.0
  [CANopenEditor/libedssharp](https://github.com/CANopenNode/CANopenEditor) (off
  limits for us — never read its code).
- **PDO mapping → signal decode is completely unserved in .NET.** Interpreting a
  candump of a CANopen machine requires Python (python-canopen/canmatrix) today.
- Stateless SDO/NMT/EMCY frame codecs: nothing managed exists.

The unfair advantage: once an object dictionary projects to `Message`/`Signal`
objects, the existing decode engine, `SignalValue`, `Database` and the candump
logreader all work unchanged.

## Reference implementation

[python-canopen](https://github.com/christiansandberg/canopen) (MIT) splits exactly
along our mission boundary: its `objectdictionary`/EDS parser and `PdoMap` are
bus-free, its `Network`/SDO client/LSS are not. The bus-free half is the behavioral
reference, diff-tested the same way cantools is for the other formats. Its MIT test
EDS files (and CANopenNode's Apache-2.0 profile EDS) are reusable as golden data
with the proper notices.

## Shape

Two new areas, mirroring the existing layout:

```
src/CanTools/Formats/Eds/       EDS + DCF reader (CiA 306)
  EdsReader.cs                  LoadFile/LoadString → ObjectDictionary
  ...tokenless INI parsing, $NODEID expression handling
src/CanTools/CanOpen/           protocol helpers (CiA 301)
  ObjectDictionary.cs           OdVariable / OdArray / OdRecord, CiA 301 data types
  CobId.cs                      function code + node id split/compose, connection set
  PdoDatabase.cs                ObjectDictionary + node id → Database (the key feature)
  NmtCommand.cs, Heartbeat.cs, Emergency.cs, SyncFrame.cs, TimeFrame.cs
  SdoFrame.cs                   expedited transfers + abort codes; segment classification
  CanOpenLogInterpreter.cs      fold a LogEntry stream into typed events (phase E4)
```

### Object dictionary model

```csharp
var od = EdsReader.LoadFile("motor.eds");          // or .dcf
var entry = od[0x6041];                            // OdVariable / OdArray / OdRecord
entry.DataType;                                    // CiA 301 enum: Unsigned16, Real32, ...
entry.AccessType;                                  // ReadOnly, ReadWrite, ...
entry.DefaultValue; entry.ParameterValue;          // DCF ParameterValue wins
od.NodeId;                                         // from [DeviceComissioning] (sic, one m)
```

`$NODEID+0x180` expressions stay unevaluated until a node id is supplied. The
misspelled `[DeviceComissioning]` section name is in the standard; accept it as-is.

### PDO projection — the core feature

```csharp
var db = PdoDatabase.Create(od, nodeId: 5);        // a plain CanTools Database
var decoded = db.DecodeMessage(0x185, frame);      // TPDO1 of node 5, existing engine
```

Rules: COB-IDs from the communication parameters (0x1400/0x1800, honoring the
bit-31 invalid flag), layout from the mapping parameters (0x1600/0x1A00, entries
packed back-to-back little-endian), types/signedness/float from the mapped entries,
names `Group.Variable` like python-canopen. EDS carries no scale/offset/unit —
signals get identity conversions; callers can overlay their own `Conversion`s.

### Frame codecs

Stateless, like `J1939`: parse/build NMT commands, heartbeat states, EMCY (with the
CiA 301 error-code table), SYNC, TIME, expedited SDO and SDO aborts. SDO
segmented/block transfers: per-frame *classification* is stateless; *reassembly* is
a stream fold (state, no I/O) and belongs in the log interpreter.

### Log interpretation (E4)

`CanOpenLogInterpreter` consumes `LogEntry` streams from the existing `LogParser`
and yields typed events: NMT state changes, emergencies, decoded PDOs, completed
SDO transfers. Zero I/O — same category as the logreader itself.

## Out of scope, permanently or until an ICanBus abstraction exists

SDO client/server engines (timeouts, retries, block windows), NMT master, heartbeat
*monitoring*, node guarding polling, SYNC production, LSS, node emulation, CiA 402
drive profiles. These need a bus and timing; they are a different library.

## Phasing (same workflow as the port: one phase per session, tests first)

- **E1** EDS/DCF reader + ObjectDictionary. Golden data: python-canopen test files
  (MIT) + CANopenNode profile EDS (Apache 2.0, with notice). Diff-test against
  python-canopen's `import_eds`.
- **E2** `CobId` + `PdoDatabase` projection. Acceptance: decode a real candump of a
  CANopen node end-to-end with existing tooling.
- **E3** Frame codecs (NMT, heartbeat, EMCY, SYNC, TIME, SDO expedited/abort).
- **E4** Log-stream interpreter (incl. segmented SDO reassembly).
- Later: DCF writing, CiA 311 XDD/XDC (XML twin of EDS, like KCD is to DBC), CPJ.

## Cautions

- "CANopen" is a CiA trademark: descriptive use in `CanTools.CanOpen` matches
  ecosystem practice (python-canopen, CANopenNode), but the README keeps the usual
  non-affiliation wording. Don't reproduce CiA spec text verbatim.
- Write our own INI parsing (small, and the OD model must be shaped for signal
  projection, not file round-tripping); do not depend on or read GPL code.
