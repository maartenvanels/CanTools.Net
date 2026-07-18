# CanKit bridge sample — design

Date: 2026-07-18
Status: Approved for planning

## Goal

A runnable proof-of-concept sample that connects **CanTools.Net** (the database /
encode-decode layer) with **CanKit** (the bus transport / hardware-I/O layer),
proving the two libraries compose into a full CAN pipeline.

The core `CanTools` library stays hardware-free and takes **no** dependency on
CanKit. The integration lives entirely in the sample.

## Scope

In scope:

- New sample project `samples/CanTools.CanKitBridge`.
- Full round trip in one direction pair: **encode → virtual bus → decode**.
- Runs against CanKit's **Virtual** adapter, so it works on any machine (incl.
  CI) with no CAN hardware.
- Added to `CanTools.slnx` so it builds in CI.

Out of scope (YAGNI):

- CAN FD, extended (29-bit) frame ids.
- Real hardware adapters (PCAN, Kvaser, SocketCAN, ZLG, …).
- Putting the bridge into the core library or shipping it as a NuGet package.
- Changes to the existing `samples/CanTools.Sample`.

## Approach

Chosen: a **separate new sample project** (over extending the existing sample or
writing an xUnit test). It shows the coupling, isolates the one reusable piece
(the frame bridge), and touches nothing existing. The existing sample keeps its
"runs with no external dependencies" promise; the new one is explicitly the
"live bus" showcase.

## Structure

`samples/CanTools.CanKitBridge/`

- `CanTools.CanKitBridge.csproj` — `Exe`, `net8.0`, `IsPackable=false`.
  References:
  - `..\..\src\CanTools\CanTools.csproj` (project reference)
  - `CanKit.Adapter.Virtual` (NuGet package, latest 0.5.x)
- `FrameBridge.cs` — the only reusable artifact. Two static methods:
  - `ToCanKit(uint id, byte[] data) -> CanFrame` via `CanFrame.Classic(id, data)`.
  - `FromCanKit(<received item>) -> (uint id, byte[] data)` reading `.ID` and the
    frame's data bytes.
  - A short comment marks where CAN FD / extended-id flags would be added.
- `Program.cs` — the end-to-end flow, reusing the same embedded DBC
  (`EngineStatus` with `EngineSpeed` / `CoolantTemp` / `GearPosition`) as the
  existing sample, so the two samples stay recognisably related.

## Data flow

```
db.EncodeMessage("EngineStatus", { EngineSpeed=2500, CoolantTemp=90, GearPosition="Third" })
    -> byte[]
    -> FrameBridge.ToCanKit(0x1F0, bytes)   -> CanFrame
    -> bus.Transmit(frame)                        [CanKit virtual bus]
    -> bus.Receive(1, timeOut)                     [same virtual bus]
    -> FrameBridge.FromCanKit(received)      -> (id, bytes)
    -> db.DecodeMessage("EngineStatus", bytes)    -> named signals -> print
```

Bus is opened with `CanBus.Open("virtual://...")` and disposed with `using`.

## Error handling

POC-level: let exceptions surface (bad decode, receive timeout). A receive
timeout is treated as a failure — the sample prints a clear message and exits
non-zero, so a broken pipeline is visible rather than silently empty.

## Verification

`dotnet run --project samples/CanTools.CanKitBridge` prints the decoded signals,
with `GearPosition = Third` as the round-trip proof (encode → bus → decode
returned the original choice label). The project also builds as part of the
solution.

## Risks / open items

- **CI now restores an external package.** Adding the project to `CanTools.slnx`
  means CI must restore `CanKit.Adapter.Virtual` from NuGet.org. Acceptable per
  the decision to include it in the solution; note it in case CI needs the feed
  configured.
- **One API detail to confirm at implementation time:** the exact property for a
  received CanKit frame's data bytes (`rec.CanFrame.Data` or equivalent) and the
  received-item wrapper type. Verify against the real `CanKit.Adapter.Virtual`
  package before finalising `FrameBridge`.
- **CanKit is pre-1.0 (0.5.x).** Its API may shift; pin the package version so
  the sample keeps building.
