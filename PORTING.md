# Porting status

Tracks what has been ported from [cantools](https://github.com/cantools/cantools)
(reference clone in `reference/cantools/`, upstream master as of July 2026).
Update this file at the end of every porting session. See `PLAN.md` for the phase plan.

| Phase | Upstream source | C# location | Status | Notes |
|-------|----------------|-------------|--------|-------|
| 1. Packing engine | `database/utils.py`, `database/conversion.py` | `src/CanTools/Packing/`, core types in `src/CanTools/` | **done** (July 2026) | see notes below |
| 2. Data model | `database/can/signal.py`, `message.py`, `node.py`, `bus.py`, `signal_group.py` | `src/CanTools/Model/` | **done** (July 2026) | attributes & containers deferred, see notes |
| 3. DBC reader | `database/can/formats/dbc.py` (parse half), `database.py`, attributes | `src/CanTools/Formats/Dbc/`, `Model/Database.cs` | **done** (July 2026) | see notes below |
| 4. DBC writer | `database/can/formats/dbc.py` (dump half) | `src/CanTools/Formats/Dbc/DbcWriter.cs` | **done** (July 2026) | see notes below |
| 5. SYM / KCD / J1939 | `formats/sym.py` (load), `formats/kcd.py` (load), `j1939.py` | `src/CanTools/Formats/Sym/`, `Formats/Kcd/`, `J1939.cs` | **done** (July 2026) | readers only; writers are follow-ups |
| 6. Log reading | `logreader.py` | `src/CanTools/Logs/` | **done** (July 2026) | see notes below |
| — | ARXML, CDD, tester, monitor, plot, c_source, CLI | — | out of scope | see PLAN.md |

## Test coverage map

Which upstream test methods have been ported (or intentionally skipped) is recorded
here per phase once work starts. Skips must have a reason.

### Phase 1 — packing engine

Ported source: `conversion.py` → `Conversion` (+ identity/linear-integer/linear/named
implementations), `namedsignalvalue.py` → `NamedSignalValue`, the encode/decode half of
`utils.py` (`encode_data`, `decode_data`, `create_encode_decode_formats`, `start_bit`,
`sawtooth_to_network_bitnum`) → `Packing/MessageCodec` + `ICodecField`. Decoded values
are modeled by the `SignalValue` struct (upstream: int | float | str | NamedSignalValue).
The bitstruct "two format strings OR-ed together" approach was replaced by direct
per-field bit addressing; behavior verified against the same golden bytes.

Ported tests:
- `test_conversion.py` — all methods → `ConversionTests`. The factory `TypeError` case
  was skipped (not representable in a typed API).
- `test_database.py::test_padding_bit_order` → `MessageCodecTests` (all 5 messages).
- `test_database.py::test_motohawk_encode_decode` — codec-level parts (scaled encode/
  decode, choices, round-trips). The by-name/strict/unknown-signal parts need `Message`
  and follow in phase 2.
- `test_database.py::test_encode_decode_no_scaling`
- `test_database.py::test_signed_dbc` — all 7 messages.
- `test_database.py::test_floating_point_dbc` — encode/decode part (f32/f64); the
  signal-property assertions follow with the DBC reader.
- `utils.decode_data` truncation/excess/size-error behavior, padding mask, and
  field-width overflow, verified against upstream via diff-test (see below).

Deviations (deliberate):
- A raw value that does not fit its field width throws `System.OverflowException`
  (message like upstream's bitstruct `OverflowError`, e.g. "Unsigned integer value 17
  out of range."). Verified with diff-test on 2026-07-15.
- A missing signal value at codec level throws `EncodeException` instead of Python's
  bare `KeyError`; phase 2 adds the message-level "is required for encoding" error.
- Choice lookup during decode only happens for integer raw values; Python would also
  match a float raw like `1.0` against choice key `1` (no known format produces this).

Not ported from `utils.py` (belongs to later phases): `format_or`/`format_and`
(message-level errors, phase 2), the `sort_signals_*`/`sort_choices_*` helpers (DBC
writer, phase 4), `prune_*_choices` (ARXML, out of scope),
`cdd_offset_to_dbc_start_bit` (CDD, out of scope).

### Phase 2 — data model + multiplexing

Ported source (`Model/`, namespace `CanTools.Model`): `signal.py` → `Signal`
(implements `ICodecField`; scale/offset/choices/is_float delegate to `Conversion`),
`message.py` → `Message` (recursive multiplexer codec tree, signal tree,
encode/decode incl. extended multiplexing, gather/assert validation, strict
bit-layout checks with upstream's exact error texts, padding with the unused bit
pattern), `node.py` → `Node`, `bus.py` → `Bus`, `signal_group.py` → `SignalGroup`.
The `str | Comments | None` comment convention became a `Comments` class with an
implicit conversion from string; `sort_signals` became the `SignalSort` delegate
with standard orderings in `SignalSorts` (omitting the parameter sorts by start
bit, like upstream's default; pass `SignalSorts.None` to keep declaration order).

Ported tests (`MessageTests`):
- `test_multiplex_extended` — signal tree, all 3 encode/decode cases, strict
  rejection of unknown signals, non-strict acceptance.
- `test_multiplex`, `test_multiplex_choices` (messages 1 and 2, incl. the empty
  choices-only multiplexer layer), `test_multiplex_bad_multiplexer` (all 4 error
  texts), `test_multiplex_choices_padding_one`, `test_padding_one`.
- `test_strict_no_multiplexer` (all 14 layout cases), `test_strict_multiplexer`
  (the valid layout and all 3 overlap cases), zero-length signal, frame id limits.
- `test_encode_signal_strict` (range checks; the in-range/out-of-range/missing-value
  parts) and `test_encode_signal_strict_negative_scaling` (all 19 parameterized
  cases with the value-table range-check bypass).
- Truncated multiplexed decode verified against upstream via diff-test on 2026-07-15.

Deferred within phase 2 scope (follow-ups, not forgotten):
- **Container messages** (`contained_messages`, `header_id`, gather/assert/encode/
  decode/unpack container paths): AUTOSAR-only feature, moves to the ARXML phase.
- **Attributes / DbcSpecifics / environment variables** (`attribute.py`,
  `attribute_definition.py`): only populated by the DBC parser, moves to phase 3.
- Signal/message mutators used by the writer (`scale`/`offset`/`choices` setters,
  `refresh` after mutation is ported as `Message.Refresh`): revisit in phase 4.

Deviations (deliberate):
- Bad multiplexer label at encode raises `EncodeException` with a descriptive
  message; upstream raises a bare `EncodeError('')` on this path.
- The "signals specified but not required" error formats the extra signal names
  sorted; upstream interpolates a Python set repr (unordered). Upstream only
  asserts the exception type here.
- Range-check error texts format numbers invariantly ("4070"); Python would render
  a float minimum as "4070.0". The ported tests use values that format identically.

### Phase 3 — DBC reader

Ported source: the parse half of `formats/dbc.py` → `Formats/Dbc/` (hand-written
tokenizer + recursive-descent parser + model builder, replacing the textparser
grammar; parse errors use upstream's exact "Invalid syntax at line X, column Y"
format), `database.py` → `Model/Database` (name/frame-id lookups with the
0x80000000 extended-id flag and frame-id masking, encode/decode by name or id,
merge of multiple files), `attribute.py`/`attribute_definition.py` →
`Model/Attribute`/`AttributeDefinition`, `environment_variable.py`,
`dbc_specifics.py` → `Model/DbcSpecifics` (incl. relation attributes),
`utils.prune_signal_choices` → `Model/ChoicePruning`.

Quirks replicated: frame id bit 31, `[0|0]` (exact text) → no range, empty unit →
null, Vector__XXX sender/receiver removal, `SystemMessageLongSymbol`/
`SystemSignalLongSymbol`/`SystemNodeLongSymbol`/`SystemEnvVarLongSymbol` long-name
resolution (incl. signal groups and relation attribute rekeying),
GenMsgCycleTime 0 → null (value and default), GenMsgSendType enum resolution,
VFrameFormat → protocol/is_fd incl. the INT-definition substitution, SG_MUL_VAL_
"3-5" tokenizing as numbers 3 and -5, mux id set-merge (sorted ascending),
message length parsed base-0, bare CM_ concatenation → synthesized Bus,
unused_bit_pattern 0xff for DBC, VECTOR__INDEPENDENT_SIG_MSG skipped.

Ported tests (`DbcReaderTests`, all priority-A cases from the upstream sweep):
vehicle, dbc_signal_initial_value, motohawk, emc32 (env vars), foobar (+ encode/
decode by name incl. 64-byte CAN FD), val_table, empty_choice, choices,
choices_issue_with_name (prune_choices), socialledge, get_message_by_*,
get_signal_by_name, timing, multiplex/multiplex_choices/multiplex_2 (extended
mux trees), attribute_Event, attributes (all four levels), j1939, floating_point,
long_names, multiple_senders, fd_detection, dbc_parse_error_messages (exact error
texts), strict_load, issue_63, issue_199(+extended), add_two_dbc_files, empty_ns.

Bulk diff-test (2026-07-15): full model dump of 22 representative dbc files
(incl. vehicle's 217 messages, both issue_184 extended-mux files, fd_test_int,
vframeformat_int, big_numbers, sig_groups) compared field-by-field against Python
cantools — zero differences. `BulkDiffTests` stays in the suite and skips unless
`CANTOOLS_MODEL_DUMP` points at a dump produced with the diff-test skill.

Deviations (deliberate):
- Python repr-string assertions are ported as property assertions.
- `//` comments are accepted up to end-of-file; upstream's tokenizer requires a
  trailing newline after a `//` comment.
- Undefined attribute names in BA_ raise `KeyNotFoundException` (upstream: bare
  `KeyError`); the composite `UnsupportedDatabaseFormatError` of the multi-format
  loader belongs to the format-dispatch layer (phase 5).
- INT/HEX attribute-definition bounds are stored as truncated doubles because
  values in the wild (big_numbers.dbc) exceed 64-bit integers.

Follow-ups: cp1252/utf-8 `errors='replace'` fidelity for undefined byte values
(test_cp1252_dbc, test_load_file_encoding) — .NET's cp1252 decoder best-fits
bytes 0x81/0x8d/0x8f/0x90/0x9d instead of replacing them; revisit if it matters.

### Phase 4 — DBC writer

Ported source: the dump half of `formats/dbc.py` → `Formats/Dbc/DbcWriter.cs` +
`LongNamesConverter.cs`, exposed as `Database.ToDbcString(sortSignals,
sortAttributeSignals, shortenLongNames)`. Where upstream deep-copies and mutates
the database, the port builds a non-destructive dump plan (sanitized/unique/
shortened names + synthesized definitions) — same output, no model mutation.

Replicated behavior: the full DBC_FMT template (CRLF everywhere, the fixed NS_
list, the empty-section quirks), name sanitation (`\W`→`_`, digit prefix) and
32-char shortening with `_%04d` collision suffixes plus SystemXxxLongSymbol
attribute/definition synthesis, attribute synthesis (GenMsgCycleTime with
delete-when-default semantics, GenSigStartValue from raw_initial, Baudrate
add/remove from the bus, VFrameFormat overwrite-but-never-delete — verified
against upstream via diff-test after the spec proved wrong on this point —
and VFrameFormat/CANFD_BRS definitions when BusType is "CAN FD"), extended
frame ids (bit 31), `M`-beats-`m<k>` multiplexer indicators with extended mux
exclusively via coalesced SG_MUL_VAL_ ranges, BO_TX_BU_ for multi-sender
messages, VAL_TABLE_ sorted descending, and Python str() number formatting.

Ported tests (`DbcWriterTests` + `DatabaseComparer`, the C# counterpart of
upstream's semantic `assert_dbc_dump`): all six golden `_dumped.dbc` pairs
(multiplex, multiplex_choices, multiplex_2, three issue_184 files), 15
dump-and-reload equivalence files (superset of upstream's dbc rows),
test_database_version, test_issue_163_dbc_newlines, test_string_attribute_
definition_dump, test_extended_id_dump, test_multiplex_dump,
test_missing_dbc_specifics, test_long_names_converter,
test_dbc_gensigstartval_from_raw_initial, test_dbc_shorten_long_names,
test_dbc_remove_special_chars.

Interop diff-test (2026-07-15): 10 files dumped by the C# writer were loaded
with Python cantools and compared against the originals via `_differences`
(include_format_specifics=False) — all equivalent, including long_names.dbc.

Deviations (deliberate): scale/offset always render with a decimal point
("1.0" where upstream writes "1") — semantically identical on reload; BA_ line
order can differ from upstream's dict-mutation order (comparisons are semantic);
the `sort_attributes`/`sort_choices` dump options are not ported (follow-up if
ever needed); unknown signal-group members are dropped at dump where upstream
raises KeyError when shortening is enabled.

### Phase 5 — KCD reader, SYM reader, J1939 helpers

Ported source: `formats/kcd.py` (load half) → `Formats/Kcd/KcdReader` (XDocument
based; KCD-offset ↔ DBC-start-bit reflection, message length "auto" with the
stable last-wins tie rule, Multiplex/MuxGroup mapping, Producer/Consumer NodeRef
resolution, default baudrate 500000, unused bit pattern 0xff, unknown
elements/attributes ignored), `formats/sym.py` (load half) → `Formats/Sym/`
(tokenizer with upstream's exact token precedence incl. `/u:` unit tokens and
`{SEND}`-in-strings, version gate "Only SYM version 6.0 is supported.", enums
with empty-but-non-null choices, signal templates incl. bit/char/string/raw
types, Var lines, sawtooth→MSB start conversion, ID ranges producing one message
per id, extended via 8-hex-digit ids or Type=Extended/FDExtended, mux
continuation sections keyed by symbol name under the first selector's name, hex
`h`-suffixed mux ids, ECU/Peripherals senders, Len default 8), and `j1939.py` →
`J1939` (frame id/PGN pack/unpack with upstream's exact error texts).

Ported tests: `KcdReaderTests` (test_the_homer incl. mux/floats/labels/big-endian/
auto lengths, all 6 encode/decode golden tests, signal_range strictness,
strict_load, empty, invalid root, node/bus lookup), `SymReaderTests` (jopp-5.0
version gate, jopp-6.0 complete model + encode/decode, empty/send/receive/
sendreceive, signal-types, variables-color-enum, empty-enum, special-chars
(cp1252), add_bad_sym_string exact parse error, multiplexed_variables signal
tree, type-extended, comments_hex_and_motorola, big-endian, strict_load +
issue_138), `J1939Tests` (all pack/unpack + all 13 error-message cases).

The float-choices case surfaced by KCD (a float signal with a value table)
removed a phase-1 deviation: integral float raw values now match integer choice
keys everywhere, like Python's dict lookup. Duplicate signal names across mux
layers (the_homer) made `Message.Refresh` last-wins like upstream.

Bulk diff-test (2026-07-15): model dump of 4 KCD + 8 SYM files (plus the 22 DBC
files) compared field-by-field against Python cantools — zero differences.

Not ported (follow-ups): the KCD and SYM writers (`as_kcd_string`,
`as_sym_string`) and their round-trip tests.

Multi-format auto-detection was ported later (July 2026):
`Formats/DatabaseLoader.LoadFile/LoadString` with the extension table, the
per-format default encodings (cp1252 for DBC/SYM, UTF-8 otherwise), probing in
upstream's order for a transparent format, and
`UnsupportedDatabaseFormatException` collecting the per-format errors with
upstream's message shape (`DBC: "...", KCD: "...", SYM: "..."`). Deviations:
only the three supported formats participate (no ARXML/CDD), and the
invalid-format-name `ValueError` cannot occur because the format is an enum.
Tests ported from `test_load_file_with_database_format` and `test_invalid_kcd`;
the KCD-of-DBC case asserts our XML wording (`syntax error: line 1, column 0`)
instead of expat's.

### Phase 6 — log reading

Ported source: `logreader.py` → `Logs/LogParser` + `LogEntry` + `TimestampFormat`.
All ten patterns with upstream's exact regexes (including the `\d+.\d+` any-char
quirk): candump plain / -tz (relative below the 1991 cutoff, absolute above) /
-l (single and double-hash CAN FD, `R` remote, trailing RT markers) / -tA
(wall-clock), and PCAN trace 1.0/1.1/1.2/1.3/2.0/2.1 (Error/Warng filtering,
RR remote records, bus-number channels, extended ids by width). Pattern
auto-detection on the first matching line, then `Parse`/`ReadLines(keepUnknowns)`/
`ReadEntries` over a `TextReader`.

Ported tests (`LogParserTests`): all TestLogreaderFormats cases (plain,
timestamped, log, absolute, ASCII-decorated, PCAN V10–V21 incl. extended id)
and the TestLogreaderStreams cases (candump skip-unparseable, PCAN V1.1 header/
error/warning skipping, PCAN V2.0 status-record skipping, CAN FD log lines).

Deviations (deliberate): timestamps are exposed as `DateTime? Timestamp` plus
`TimeSpan? TimeOffset` instead of Python's union type; unix timestamps convert
to local time by parsing the seconds text decimally (exact microseconds); the
upstream `tz` parameter is not ported — the timezone-sensitive assertions are
asserted against the same local-time conversion, making the tests timezone
independent.

## API polish (July 2026, post-port)

A style pass brought the public surface in line with .NET conventions; behavior
is unchanged (all ported tests green, writer output re-verified against Python
cantools including relation attributes). Renames and changes:

- `Signal.Start` / `ICodecField.Start` → `StartBit`.
- `Database.AsDbcString` → `ToDbcString`.
- Python-repr style `ToString()` overrides replaced with .NET-style debug text.
- `DbcSpecifics` now exposes `IReadOnlyDictionary`; the nested relation
  attribute dictionaries were flattened to `IReadOnlyList<RelationAttribute>`
  entries with `SignalName`/`NodeName`/`Attribute`.
- `Database` gained `TryGetMessageByName`/`TryGetMessageByFrameId`, helpful
  lookup error messages, and `allowExcess` on `DecodeMessage`.
- Unused setters removed (`Node.Name`, `Attribute.Value`,
  `AttributeDefinition.DefaultValue`, `EnvironmentVariable.Comment`,
  `Message.CycleTime`); `Dbc` properties are init-only.
- `Message.Refresh(bool?)` split into `Refresh()`/`Refresh(bool)`;
  `Message.Receivers` is computed once per refresh; encode no longer allocates
  a padding mask unless `padding: true`.
- One public type per file (J1939 records, TimestampFormat, SYM tokenizer/
  statements split out).

## Design review cleanup (July 2026)

A structural pass after a design review; behavior unchanged except where noted
(all tests green throughout). Changes:

- The internal `Conversion` subclasses moved into `Conversion.cs` (they are a
  closed set only `Conversion.Create` constructs); the non-choices `ScaledToRaw`
  guard became a base implementation; `NamedSignalConversion.TryGetChoice` and
  `Conversion.RoundUnlessFloat` now serve `MessageCodec`, which previously
  re-implemented both the no-scaling choice lookup and the banker's rounding.
- `BitNumbering.SawtoothToNetwork` replaces six copies of the sawtooth↔network
  bit formula (`SignalSorts`, `Message`, `KcdReader`, `SymBuilder`, twice in
  `MessageCodec`).
- `Formats/PythonInt` replaces three divergent `int(s, 0)` parsers. The DBC
  message length and KCD frame id are now parsed with Python's exact rules
  (0x/0o/0b prefixes, sign, leading-zero rejection) instead of a laxer
  `long.Parse` — stricter, and closer to upstream.
- `Formats/FormatEncodings` owns cp1252 and the code-pages registration;
  `SymReader` gained its own `DefaultEncoding` instead of borrowing
  `DbcReader`'s.
- `ParseException` moved from the root namespace to `CanTools.Formats`, next to
  `UnsupportedDatabaseFormatException` (it was only ever thrown by parsers).
- `RelationAttribute` is immutable again: the long-name rekey in `DbcBuilder`
  rebuilds entries instead of mutating `SignalName` through an internal setter.
- `SignalSorts.ByName` was removed (never used); `SignalSorts.None` documents
  that it is recognized by identity (the port of Python's `sort_signals=None`),
  and the DBC writer checks it via `SignalSorts.KeepsDeclarationOrder`.
- `EncodeException` gained the `(message, innerException)` constructor the rest
  of the exception family already had.
- CANopen: `OdArray`/`OdRecord` share the new `OdComposite` base (the member
  dictionaries were duplicated); `CanOpenFrames` centralizes the frame length
  error format and python-canopen's "Code 0x…, Description" text (EMCY and
  `SdoAbort` had copies); `SdoBlockFrame` now exposes the wire layout
  (`Command`, `IsCarrierSide`, `IsEnd`, `PaddingCount`, `SubCommand`,
  `AckSequence`, `Multiplexer`) so `CanOpenLogInterpreter` no longer parses
  block frame bytes by hand.
- CANopen naming: `Heartbeat` → `HeartbeatMessage` (aligning with the other
  frame codecs), and the frame-wrapping `CanOpenEvent` properties are named
  after their protocol object (`Nmt`, `Emergency`, `Sync`, `Time`) so
  `Message` only ever means `Model.Message` (`PdoEvent.Message`).

Considered and deliberately skipped: merging the tiny CANopen enum files
(`NmtState`, `CanOpenFunction`, `SdoDirection`) — the one-public-type-per-file
rule wins; renaming `NamedSignalValue` — it is upstream's name and the
PORTING.md mapping builds on it; routing `PdoDatabase.PdoName` through
`CobId.Function` — the name formula is deliberately python-canopen's and also
applies to non-connection-set COB-IDs where `CobId` would classify differently;
a shared DBC/SYM tokenizer base — two similar hand-written tokenizers are not
yet a pattern worth an abstraction; the multi-language `Comments` machinery
stays for the planned ARXML phase.
