# CANopen live SDO-client — ontwerp

**Datum:** 2026-07-18
**Status:** goedgekeurd (ontwerp), klaar voor implementatieplan
**Sub-project van:** een managed-.NET CANopen runtime-laag bovenop CanTools.Net

## Achtergrond en motivatie

CanTools.Net is vandaag een *offline* bibliotheek: het parseert databasebestanden
(DBC/KCD/SYM/EDS/DCF), (de)codeert payloads, leest logs en vouwt een candump om
naar getypte CANopen-events met SDO-reassembly. Het praat niet live met een bus,
op een experimentele CanKit-bridge in `samples/CanTools.CanKitBridge` na.

[lely-core](https://gitlab.com/lely_industries/lely-core) (Apache 2.0) is een
volwassen, CiA-conformance-geteste *runtime* CANopen-stack in C/C++. De twee
bibliotheken overlappen in datamodel maar verschillen in doel: CanTools.Net
*beschrijft en analyseert* een netwerk; lely-core *bedient* het.

Dit sub-project zet de eerste stap richting live bus: een **actieve SDO-client**
in pure .NET, bovenop de bestaande SDO-codecs. De correctheid wordt bewezen door
testvectoren die van lely's testsuite worden *afgekeken* (niet overgenomen) plus
een optionele live loopback tegen een echte lely SDO-server.

### Licentie-uitgangspunt

lely-core is Apache 2.0; CanTools.Net is MIT. lely-code wordt **niet** verbatim
overgenomen of geherlicentieerd. Wel:

- Gedrag en testscenario's worden *afgekeken* en opnieuw geschreven in C#.
- Elk geport testbestand krijgt een headercommentaar met Apache 2.0-attributie
  naar lely-core, plus een regel in `THIRD-PARTY-NOTICES.txt`.
- De optionele loopback koppelt tegen een apart gebouwde lely-binary; dat is een
  distributie-/interop-kwestie, geen broncode-vermenging.

## Scope

### In scope (v1)

- `CanFrame` readonly struct + `ICanChannel`-abstractie in de core (geen externe
  dependency).
- `InMemoryCanChannel` scriptbare fake voor tests.
- Extractie van de gedeelde SDO byte-logica (`SdoTransferCodec`) uit de bestaande
  log-interpreter.
- `SdoClient`: upload (lezen) en download (schrijven) van een remote node, met
  **expedited**, **segmented** én **block** transfer.
- Getypte lees-/schrijfhelper bovenop `byte[]`, hergebruikt bestaande
  `CanOpenDataType`/`OdValue`.
- Getypte exceptie-hiërarchie bovenop de bestaande `SdoAbortCode`.
- Tier-1-tests: van lely afgekeken SDO-vectoren, data-driven, cross-platform.
- `CanTools.CanKit`-adapter als project/referentie-sample (promotie van de
  huidige sample-bridge). **Geen** gepubliceerd NuGet-pakket in v1 — de
  releaseset blijft `CanTools.Net` + `CanTools.Net.Cli`.
- Tier-2-tests: optionele live loopback tegen native lely over vcan.

### Uit scope (v1, mogelijke follow-ups)

- PDO-runtime (TPDO/RPDO), NMT-master, heartbeat/node-guarding-monitor,
  EMCY-consumer.
- LSS (CiA 305), CiA 309 gateway.
- SDO-*server* (wij als slave), device-simulatie vanuit EDS.
- CAN FD en 29-bit extended id in de SDO-laag (struct draagt de flags al, maar
  v1 test alleen classic 11-bit).
- P/Invoke-wrapper over lely als runtime (bewust niet: botst met de
  dependency-vrije identiteit).

## Architectuur

Rolscheiding is leidend: de bestaande log-interpreter is **passief** (observeert
verkeer, ziet beide kanten); de nieuwe client is **actief** (genereert requests,
drijft de uitwisseling, doet timeouts/retries). Ze delen de lastige byte-logica,
niet de aansturing (ontwerpkeuze A).

### Componenten

| Component | Laag | Afhankelijkheden | Verantwoordelijkheid |
|---|---|---|---|
| `CanFrame` (readonly struct) | core | geen | Minimale frame-representatie: `Id`, `Data`, `IsExtended`, `IsFd`. |
| `ICanChannel` | core | geen | Enige koppeling codec-laag ↔ bus: `SendAsync` + ontvangkant. |
| `InMemoryCanChannel` | tests | geen | Scriptbare fake; server-antwoorden op volgorde. |
| `SdoTransferCodec` (intern) | core | — | Gedeelde SDO command-byte codering + segment/block reassemblybuffers, geëxtraheerd uit de interpreter. |
| `SdoClient` | core | `ICanChannel`, `SdoTransferCodec` | Actieve state-machine: up/download, expedited→segmented→block, timeouts, abort. |
| `CanTools.CanKit` | apart project (niet gepubliceerd in v1) | CanKit | `ICanChannel`-adapter over CanKit. Enige plek met CanKit-dependency; referentie-adapter, geen NuGet-release in v1. |

### `CanFrame`

Sluit aan op de huidige `(uint frameId, byte[] payload)`-representatie uit
`FrameBridge`. Velden: `uint Id`, `byte[] Data`, `bool IsExtended`, `bool IsFd`.
v1 gebruikt classic 11-bit; de flags staan er alvast in zodat FD/29-bit later
niet-brekend toegevoegd kan worden.

### `ICanChannel`

```csharp
public interface ICanChannel
{
    ValueTask SendAsync(CanFrame frame, CancellationToken ct = default);
    ValueTask<CanFrame> ReceiveAsync(CancellationToken ct = default);
}
```

De ontvangkant is een `ReceiveAsync` met cancellation; timeouts worden door de
`SdoClient` opgelegd via een gelinkte `CancellationTokenSource`. (Een
`IAsyncEnumerable`-variant kan later, maar is niet nodig voor v1.)

## Publieke API van `SdoClient`

```csharp
var client = new SdoClient(channel, nodeId: 0x0A, new SdoClientOptions {
    Timeout = TimeSpan.FromMilliseconds(500),
    EnableBlockTransfer = true,   // valt terug op segmented als de server het niet ondersteunt
});

byte[] raw = await client.UploadAsync(index: 0x1018, subIndex: 1, ct);   // lezen
await client.DownloadAsync(0x2000, 0, BitConverter.GetBytes(42), ct);     // schrijven
```

- Ruwe `byte[]` als basis; daarbovenop een getypte helper die de bestaande
  `CanOpenDataType`/`OdValue` hergebruikt, zodat een int/string/etc. terugkomt
  i.p.v. bytes.
- COB-ID's volgens CiA 301 default: `0x600 + nodeId` (client→server, alle
  requests) en `0x580 + nodeId` (server→client, alle responses).
- Eén uitstaande transfer per client-instantie (SDO is serieel per server-node),
  bewaakt met een semaphore.

## Data flow

1. Caller roept `UploadAsync`/`DownloadAsync`.
2. `SdoClient` bouwt het initiate-frame via `SdoTransferCodec` en verstuurt het
   met `channel.SendAsync`.
3. Client ontvangt frames via `channel.ReceiveAsync`, filtert op de
   server-COB-ID (frames van andere ids worden genegeerd).
4. Afhankelijk van de server-respons drijft de client expedited (klaar),
   segmented of block transfer, waarbij `SdoTransferCodec` de segmenten/blokken
   assembleert.
5. Het geassembleerde resultaat (of de bevestiging bij download) komt terug bij
   de caller.

## Foutafhandeling

Getypte excepties bovenop de bestaande `SdoAbortCode`:

| Situatie | Exceptie |
|---|---|
| Geen antwoord binnen `Timeout` | `SdoTimeoutException` |
| Server stuurt abort | `SdoAbortException` (wrapt `SdoAbortCode`) |
| Toggle-bit mismatch / onverwachte command specifier / protocolschending | `SdoProtocolException` |
| Caller annuleert | abort naar server + `OperationCanceledException` |

Alle drie de nieuwe excepties erven van een gemeenschappelijke basis die weer
onder de bestaande `CanToolsException` valt, consistent met de huidige
exceptie-structuur.

## Teststrategie

### Tier 1 — altijd aan, cross-platform

Van lely's C-testbronnen afgekeken SDO-scenario's, herschreven als data-driven
xUnit-vectoren: byte-sequenties (client-request → server-response(s)) plus
verwacht resultaat of abort-code, gedraaid tegen `InMemoryCanChannel`. Dekt
expedited, segmented, block én abort-paden. Elk geport bestand draagt een
Apache 2.0-attributieheader; `THIRD-PARTY-NOTICES.txt` wordt bijgewerkt.

### Tier 2 — optioneel, integratie

Live loopback: `SdoClient` praat via de `CanTools.CanKit`-adapter over een
virtuele bus (vcan, Linux) tegen een echte, native-gebouwde lely SDO-server.
Gemarkeerd met `[Trait("Category","Interop")]`; overgeslagen tenzij lely + vcan
aanwezig zijn. Draait in een aparte CI-job of lokaal, en blokkeert de bestaande
cross-platform CI niet.

## Milestone-volgorde

1. `CanFrame` + `ICanChannel` + `InMemoryCanChannel`.
2. `SdoTransferCodec` extraheren uit de interpreter (refactor; interpreter-tests
   blijven groen als vangnet).
3. `SdoClient` expedited up/download + vectoren.
4. Segmented + vectoren.
5. Block transfer + vectoren.
6. `CanTools.CanKit`-adapterproject + referentie-sample (geen NuGet-release).
7. Optionele live-loopback-harnas.

## Risico's en aandachtspunten

- **Refactor van getptste code (milestone 2):** de interpreter-tests moeten voor
  én na de extractie groen blijven; dat is het vangnet.
- **Block transfer** is de meest complexe state-machine; het is bewust achteraan
  gezet zodat expedited/segmented eerder waarde leveren.
- **Native lely + vcan** is Linux-gebonden; daarom optioneel gehouden om de
  huidige cross-platform CI niet te breken.
- **Afkijken vs. overnemen:** discipline op de licentie — scenario's opnieuw
  schrijven, altijd attribueren, nooit C-broncode kopiëren.
