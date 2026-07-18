# CanKit Bridge Sample Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A runnable sample that sends a CanTools.Net-encoded frame across a CanKit virtual bus and decodes it back, proving the two libraries compose.

**Architecture:** A new console sample project `samples/CanTools.CanKitBridge` references the core `CanTools` project and the CanKit NuGet packages. A small `FrameBridge` helper converts between CanTools' `(uint frameId, byte[] payload)` and CanKit's `CanFrame`. `Program.cs` runs encode → transmit (CanKit) → receive (CanKit) → decode end to end on the in-process virtual adapter.

**Tech Stack:** C# / .NET 8, CanTools.Net (project reference), CanKit.Core + CanKit.Adapter.Virtual 0.5.5 (NuGet).

## Global Constraints

- Target framework: `net8.0`; `Nullable` enabled; `ImplicitUsings` enabled; `IsPackable=false` — matching `samples/CanTools.Sample/CanTools.Sample.csproj`.
- The core `CanTools` library takes **no** dependency on CanKit. CanKit references live only in the new sample project.
- Do **not** modify `samples/CanTools.Sample`.
- CanKit is pre-1.0; pin both packages to `0.5.5` (exact version) so the build stays stable.
- **CanKit API-verification note:** the CanKit type/member names below (`CanBus.Open`, `CanFrame.Classic`, `CanFrame.ID`, `CanFrame.Data`, `Transmit`, `FrameObserved`/`e.CanFrame`) are taken from the CanKit 0.5.x README and QuickStartTxRx sample but have not been compiled here. Task 1 restores the packages; if any name resolves differently in the pinned version, adjust it when the build/run step reports it — this is expected, not a plan defect.

---

### Task 1: Scaffold the sample project and wire it into the solution

**Files:**
- Create: `samples/CanTools.CanKitBridge/CanTools.CanKitBridge.csproj`
- Create: `samples/CanTools.CanKitBridge/Program.cs` (temporary stub, replaced in Task 3)
- Modify: `CanTools.slnx`

**Interfaces:**
- Produces: a buildable project named `CanTools.CanKitBridge` that restores `CanKit.Core` and `CanKit.Adapter.Virtual`.

- [ ] **Step 1: Create the csproj**

Create `samples/CanTools.CanKitBridge/CanTools.CanKitBridge.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CanTools\CanTools.csproj" />
    <PackageReference Include="CanKit.Core" Version="0.5.5" />
    <PackageReference Include="CanKit.Adapter.Virtual" Version="0.5.5" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create a stub Program.cs**

Create `samples/CanTools.CanKitBridge/Program.cs`:

```csharp
// Placeholder — replaced in Task 3.
System.Console.WriteLine("CanKit bridge sample");
```

- [ ] **Step 3: Add the project to the solution**

Edit `CanTools.slnx`. Inside the existing `/samples/` folder element, add the new project next to `CanTools.Sample`:

```xml
  <Folder Name="/samples/">
    <Project Path="samples/CanTools.Sample/CanTools.Sample.csproj" />
    <Project Path="samples/CanTools.CanKitBridge/CanTools.CanKitBridge.csproj" />
  </Folder>
```

- [ ] **Step 4: Restore and build the new project**

Run: `dotnet build samples/CanTools.CanKitBridge/CanTools.CanKitBridge.csproj -v quiet`
Expected: `Build succeeded.` and the CanKit packages restore from NuGet.org. If restore fails because the feed is missing, add nuget.org to the environment's NuGet sources, then re-run.

- [ ] **Step 5: Commit**

```bash
git add samples/CanTools.CanKitBridge/CanTools.CanKitBridge.csproj samples/CanTools.CanKitBridge/Program.cs CanTools.slnx
git commit -m "Scaffold the CanKit bridge sample project"
```

---

### Task 2: Implement FrameBridge

**Files:**
- Create: `samples/CanTools.CanKitBridge/FrameBridge.cs`

**Interfaces:**
- Consumes: CanKit `CanFrame` (`CanFrame.Classic(int id, byte[] payload)`, `frame.ID`, `frame.Data` as `ReadOnlyMemory<byte>`).
- Produces:
  - `FrameBridge.ToCanKit(uint frameId, byte[] payload) -> CanFrame`
  - `FrameBridge.FromCanKit(CanFrame frame) -> (uint FrameId, byte[] Payload)`

- [ ] **Step 1: Write FrameBridge.cs**

Create `samples/CanTools.CanKitBridge/FrameBridge.cs`:

```csharp
using CanKit.Core.Definitions;

namespace CanTools.CanKitBridge;

/// <summary>
/// Bridges CanTools.Net's (frame id, payload) representation and CanKit's
/// <see cref="CanFrame"/>. Classic 11-bit data frames only. To support CAN FD
/// or extended (29-bit) ids, pass the extended flag to CanFrame.Classic (its
/// third parameter) or switch to CanFrame.Fd here, and carry the flag through
/// FromCanKit alongside frame.ID.
/// </summary>
internal static class FrameBridge
{
    public static CanFrame ToCanKit(uint frameId, byte[] payload) =>
        CanFrame.Classic((int)frameId, payload);

    public static (uint FrameId, byte[] Payload) FromCanKit(CanFrame frame) =>
        ((uint)frame.ID, frame.Data.ToArray());
}
```

Note: if the pinned CanKit version puts `CanFrame` in a different namespace, the build in the next step will report it — change the `using` accordingly (README shows `CanKit.Core` / `CanKit.Core.Definitions`).

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build samples/CanTools.CanKitBridge/CanTools.CanKitBridge.csproj -v quiet`
Expected: `Build succeeded.` (the stub `Program.cs` from Task 1 does not reference `FrameBridge` yet, so this only checks that `FrameBridge` compiles against the real CanKit API.)

- [ ] **Step 3: Commit**

```bash
git add samples/CanTools.CanKitBridge/FrameBridge.cs
git commit -m "Add FrameBridge between CanTools payloads and CanKit frames"
```

---

### Task 3: Implement the end-to-end flow and verify by running

**Files:**
- Modify: `samples/CanTools.CanKitBridge/Program.cs` (replace the stub)

**Interfaces:**
- Consumes: `FrameBridge.ToCanKit`, `FrameBridge.FromCanKit` (Task 2); `DbcReader.LoadString`, `Database.GetMessageByName`, `Database.EncodeMessage(string, dict)`, `Database.DecodeMessage(uint, ReadOnlySpan<byte>)`, `Message.FrameId` (existing core API); CanKit `CanBus.Open`, `bus.Transmit`, `bus.FrameObserved`.

- [ ] **Step 1: Replace Program.cs with the full flow**

Replace the contents of `samples/CanTools.CanKitBridge/Program.cs`:

```csharp
// End-to-end proof that CanTools.Net (encode/decode) and CanKit (bus transport)
// compose: encode a frame with CanTools, push it across a CanKit virtual bus,
// receive it on a second handle, and decode it back with CanTools.
//
//   dotnet run --project samples/CanTools.CanKitBridge

using System.Globalization;
using CanKit.Core;
using CanKit.Core.Definitions;
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
rxBus.FrameObserved += (_, e) =>
{
    receivedFrame = e.CanFrame;
    gotFrame.Set();
};

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
```

Note: `e.CanFrame` and `CanFrame` are treated as a value type here (`CanFrame? ... .Value`). If the pinned CanKit version makes `CanFrame` a reference type, drop `.Value` in step 5 and use `FromCanKit(receivedFrame)` — the build will flag it.

- [ ] **Step 2: Build**

Run: `dotnet build samples/CanTools.CanKitBridge/CanTools.CanKitBridge.csproj -v quiet`
Expected: `Build succeeded.` If a CanKit member name differs, fix per the API-verification note, then rebuild.

- [ ] **Step 3: Run the sample and verify the round trip**

Run: `dotnet run --project samples/CanTools.CanKitBridge`
Expected output (order of decoded signals as produced by DecodeMessage):

```
Encoded 0x1F0: <hex>
Decoded 0x1F0 off the bus:
  EngineSpeed = 2500
  CoolantTemp = 90
  GearPosition = Third

Round trip OK: GearPosition = Third survived encode -> bus -> decode.
```

Exit code 0. If the sample prints "No frame received", the two virtual handles are not sharing the channel — first try a single endpoint for both (already the case) and confirm the receiver is subscribed before `Transmit`; if the virtual adapter needs a bitrate, open with `CanBus.Open("virtual://alpha/0", cfg => cfg.Baud(500000))` on both handles.

- [ ] **Step 4: Commit**

```bash
git add samples/CanTools.CanKitBridge/Program.cs
git commit -m "Wire the CanKit bridge sample end to end"
```

---

## Verification (whole plan)

- `dotnet build` on the solution succeeds with the new project included.
- `dotnet run --project samples/CanTools.CanKitBridge` exits 0 and prints `GearPosition = Third`.
- The core `CanTools` project and `samples/CanTools.Sample` are unchanged (no CanKit reference leaked into them).
