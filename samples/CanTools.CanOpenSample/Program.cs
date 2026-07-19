// End-to-end tour of the CanTools.Net live SDO client (CANopen), driven by an
// object dictionary loaded from an EDS file.
//
// A real SDO exchange needs a CANopen node on the bus to answer. To keep this
// sample self-contained — no hardware, no external server — it talks to a small
// in-process SimulatedSdoNode whose memory is seeded from the EDS default values.
// On a real bus you would swap SimulatedSdoNode for a channel bound to your CAN
// interface (e.g. the CanKitCanChannel adapter in samples/CanTools.CanKitBridge),
// and the SdoClient code below is unchanged.
//
// The dictionary carries each entry's index, subindex and CiA 301 data type, so
// the calls below name a parameter instead of spelling out raw indices and type
// codes. Point the sample at a real vendor EDS to read and list its parameters:
//
//   dotnet run --project samples/CanTools.CanOpenSample -- path/to/device.eds
//
// With no argument it uses the bundled VirtualNode.eds (a made-up device).

using System.Globalization;
using CanTools.CanOpen;
using CanTools.CanOpenSample;
using CanTools.Formats.Eds;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

const byte nodeId = 0x0A;

var edsPath = args.Length > 0
    ? args[0]
    : Path.Combine(AppContext.BaseDirectory, "VirtualNode.eds");
var usingBundledNode = args.Length == 0;

var dictionary = EdsReader.LoadFile(edsPath, nodeId);

// The simulated node serves the values described by the dictionary; its memory
// starts from the EDS default values.
var node = SimulatedSdoNode.SeededFrom(nodeId, dictionary);
using var client = new SdoClient(node, nodeId);

Console.WriteLine($"Loaded {Path.GetFileName(edsPath)} — talking SDO to node 0x{nodeId:X2}.\n");
PrintParameterList(dictionary);

if (usingBundledNode)
{
    return await RunVirtualNodeTour(client, dictionary) ? 0 : 1;
}

await ReadStandardEntries(client, dictionary);
return 0;

// The dictionary as a parameter list: index/subindex, name, type and access.
static void PrintParameterList(ObjectDictionary dictionary)
{
    Console.WriteLine("Parameters:");

    foreach (var variable in dictionary.Entries.OfType<OdVariable>().Take(12))
    {
        Console.WriteLine(
            $"  0x{variable.Index:X4}/{variable.Subindex} {variable.Name,-28} "
            + $"{variable.DataType,-14} {variable.AccessType}");
    }

    Console.WriteLine();
}

// Reads, writes and programs the bundled virtual node entirely by parameter name —
// no raw indices or CiA 301 type codes appear here; they come from the dictionary.
static async Task<bool> RunVirtualNodeTour(SdoClient client, ObjectDictionary od)
{
    // 1. Expedited read (the value fits in four bytes).
    var deviceType = await client.UploadAsync(od, "Device type");
    Console.WriteLine($"  read  \"Device type\"              = 0x{deviceType.ToUInt64():X8}");

    // 2. Segmented read (the string is longer than four bytes).
    var deviceName = await client.UploadAsync(od, "Manufacturer device name");
    Console.WriteLine($"  read  \"Manufacturer device name\" = \"{deviceName.Text}\"");

    // 3. Write then read back a parameter, both by name.
    await client.DownloadAsync(od, "Sample parameter", (OdValue)1234UL);
    var readBack = await client.UploadAsync(od, "Sample parameter");
    Console.WriteLine($"  write \"Sample parameter\" = 1234, read back = {readBack.ToUInt64()}");

    // 4. Programming a device: push a set of parameters, one after another. The
    //    order is the caller's; here it follows the configuration below.
    Console.WriteLine("\n  Programming from a configuration:");
    var configuration = new (string Name, OdValue Value)[]
    {
        ("Sample parameter", (OdValue)4095UL),
        ("Temperature offset", (OdValue)(-5)),
    };

    foreach (var (name, value) in configuration)
    {
        await client.DownloadAsync(od, name, value);
        var written = await client.UploadAsync(od, name);
        Console.WriteLine($"    program \"{name}\" = {value} -> read back {written}");
    }

    // Persist the freshly written values with the standard store command (0x1010).
    await client.StoreParametersAsync();
    Console.WriteLine("    store parameters (0x1010/1 \"save\") -> ack");

    // 5. Reading an entry the node does not serve surfaces the server's abort as a
    //    typed exception ("Error register" is in the EDS but has no default).
    try
    {
        await client.UploadAsync(od, "Error register");
    }
    catch (SdoAbortException ex)
    {
        Console.WriteLine($"\n  read  \"Error register\" -> aborted: {ex.Code} (0x{(uint)ex.Code:X8})");
    }

    var ok = deviceType.ToUInt64() == 0x000F0191
             && deviceName.Text == "CanTools.Net Virtual Node";

    Console.WriteLine(ok
        ? "\nExpedited + segmented + typed SDO round trip, driven by the EDS: OK"
        : "\nRound trip FAILED.");

    return ok;
}

// For a real vendor EDS: any CANopen device answers 0x1000 Device Type, and most
// also carry 0x1008. Read them by index, taking the name and data type from the
// EDS — no per-device type codes in the calling code.
static async Task ReadStandardEntries(SdoClient client, ObjectDictionary od)
{
    foreach (var index in new[] { CanOpenObjects.DeviceType, CanOpenObjects.ManufacturerDeviceName })
    {
        if (od.GetVariable(index) is not { } variable)
        {
            continue;
        }

        try
        {
            var value = await client.UploadAsync(variable);
            Console.WriteLine($"  read  0x{index:X4} {variable.Name,-28} = {value}");
        }
        catch (SdoAbortException ex)
        {
            Console.WriteLine($"  read  0x{index:X4} {variable.Name,-28} -> {ex.Code}");
        }
    }

    Console.WriteLine(
        "\n  The same DownloadAsync(dictionary, name, value) overload programs a "
        + "device from this parameter list.");
}
