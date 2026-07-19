using System.Text;
using System.Text.RegularExpressions;
using CanTools.CanOpen;

namespace CanTools.Formats.Eds;

/// <summary>
/// Reads CANopen EDS and DCF files (CiA 306) into an <see cref="ObjectDictionary"/>,
/// behavior-matched to python-canopen's import_eds.
/// </summary>
public static partial class EdsReader
{
    private static readonly HashSet<string> StandardOptions =
    [
        "ObjectType", "ParameterName", "DataType", "AccessType", "PDOMapping",
        "LowLimit", "HighLimit", "DefaultValue", "ParameterValue", "Factor",
        "Description", "Unit", "StorageLocation", "CompactSubObj", "SubNumber",
    ];

    private static readonly int[] StandardBaudrates = [10, 20, 50, 125, 250, 500, 800, 1000];

    [GeneratedRegex("^[0-9A-Fa-f]{4}$")]
    private static partial Regex IndexSection();

    [GeneratedRegex("^([0-9A-Fa-f]{4})[Ss]ub([0-9A-Fa-f]+)$")]
    private static partial Regex SubindexSection();

    [GeneratedRegex("^([0-9A-Fa-f]{4})Name")]
    private static partial Regex NameSection();

    [GeneratedRegex(@"\+?\$NODEID\+?")]
    private static partial Regex NodeIdToken();

    public static ObjectDictionary LoadFile(string path, int? nodeId = null, Encoding? encoding = null)
    {
        return LoadString(File.ReadAllText(path, encoding ?? Encoding.UTF8), nodeId);
    }

    public static ObjectDictionary LoadString(string text, int? nodeId = null)
    {
        var ini = IniFile.Parse(text);
        var od = new ObjectDictionary { SourceDocument = IniDocument.Parse(text) };

        if (ini.Find("Comments") is { } comments)
        {
            var count = (int)PythonInt.Parse(comments.GetValueOrNull("Lines") ?? "0");
            od.Comments = string.Join('\n', Enumerable.Range(1, count).Select(i =>
                comments.GetValueOrNull($"Line{i}")
                ?? throw new ParseException($"[Comments] is missing Line{i}.")));
        }

        if (ini.Find("DeviceInfo") is { } deviceInfo)
        {
            LoadDeviceInformation(deviceInfo, od.DeviceInformation);
        }

        // The DeviceComissioning section name is misspelled in the standard itself.
        if (ini.Find("DeviceComissioning") is { } commissioning)
        {
            if (commissioning.GetValueOrNull("Baudrate") is { } baudrate
                && int.Parse(baudrate) is var kbit && kbit != 0)
            {
                od.Bitrate = kbit * 1000;
            }

            // An explicit node id wins over the one in the file.
            if (nodeId is null && commissioning.GetValueOrNull("NodeID") is { } id)
            {
                nodeId = (int)PythonInt.Parse(id);
            }

            od.NodeId = nodeId;
        }

        foreach (var section in ini.Sections)
        {
            if (section.Name is "DummyUsage" or "Dummyusage" or "dummyUsage" or "dummyusage")
            {
                LoadDummies(section, od);
                continue;
            }

            if (IndexSection().IsMatch(section.Name))
            {
                LoadIndexSection(ini, section, od, nodeId);
                continue;
            }

            if (SubindexSection().Match(section.Name) is { Success: true } subMatch)
            {
                LoadSubindexSection(ini, section, subMatch, od, nodeId);
                continue;
            }

            if (NameSection().Match(section.Name) is { Success: true } nameMatch)
            {
                LoadNameSection(section, nameMatch, od);
            }
        }

        return od;
    }

    private static void LoadDeviceInformation(IniSection section, DeviceInformation info)
    {
        var baudrates = new HashSet<int>();

        foreach (var rate in StandardBaudrates)
        {
            if (PythonInt.Parse(section.GetValueOrNull($"BaudRate_{rate}") ?? "0") != 0)
            {
                baudrates.Add(rate * 1000);
            }
        }

        info.AllowedBaudrates = baudrates;
        info.VendorName = section.GetValueOrNull("VendorName");
        info.VendorNumber = OptionalInteger(section, "VendorNumber");
        info.ProductName = section.GetValueOrNull("ProductName");
        info.ProductNumber = OptionalInteger(section, "ProductNumber");
        info.RevisionNumber = OptionalInteger(section, "RevisionNumber");
        info.OrderCode = section.GetValueOrNull("OrderCode");
        info.SimpleBootUpMaster = OptionalFlag(section, "SimpleBootUpMaster");
        info.SimpleBootUpSlave = OptionalFlag(section, "SimpleBootUpSlave");
        info.Granularity = OptionalFlag(section, "Granularity");
        info.DynamicChannelsSupported = OptionalFlag(section, "DynamicChannelsSupported");
        info.GroupMessaging = OptionalFlag(section, "GroupMessaging");
        info.RpdoCount = (int?)OptionalInteger(section, "NrOfRXPDO");
        info.TpdoCount = (int?)OptionalInteger(section, "NrOfTXPDO");
        info.LssSupported = OptionalFlag(section, "LSS_Supported");
    }

    private static long? OptionalInteger(IniSection section, string key) =>
        section.GetValueOrNull(key) is { } value ? (long)PythonInt.Parse(value) : null;

    private static bool? OptionalFlag(IniSection section, string key) =>
        section.GetValueOrNull(key) is { } value ? PythonInt.Parse(value) != 0 : null;

    private static void LoadDummies(IniSection section, ObjectDictionary od)
    {
        for (var index = 1; index <= 7; index++)
        {
            var key = $"Dummy{index:d4}";
            var value = section.GetValueOrNull(key)
                ?? throw new ParseException($"[{section.Name}] is missing {key}.");

            if (int.Parse(value) == 1)
            {
                od.Add(new OdVariable(index, 0, key)
                {
                    DataType = (CanOpenDataType)index,
                    AccessType = "const",
                });
            }
        }
    }

    private static void LoadIndexSection(
        IniFile ini, IniSection section, ObjectDictionary od, int? nodeId)
    {
        var index = Convert.ToInt32(section.Name, 16);
        var objectType = (int)PythonInt.Parse(section.GetValueOrNull("ObjectType") ?? "0x07");

        switch (objectType)
        {
            case 0x07 or 0x02: // VAR or DOMAIN
                od.Add(BuildVariable(ini, section, nodeId, objectType, index, 0));
                break;
            case 0x08: // ARRAY
                var array = new OdArray(index, RequiredOption(section, "ParameterName"))
                {
                    StorageLocation = section.GetValueOrNull("StorageLocation"),
                    CustomOptions = CustomOptions(section),
                };

                // A compact array defines subindex 1 inline; the other members are
                // synthesized from it on access.
                if (section.Options.ContainsKey("CompactSubObj"))
                {
                    array.AddMember(new OdVariable(index, 0, "Number of entries")
                    {
                        DataType = CanOpenDataType.Unsigned8,
                    });
                    array.AddMember(BuildVariable(ini, section, nodeId, objectType, index, 1));
                }

                od.Add(array);
                break;
            case 0x09: // RECORD
                od.Add(new OdRecord(index, RequiredOption(section, "ParameterName"))
                {
                    StorageLocation = section.GetValueOrNull("StorageLocation"),
                    CustomOptions = CustomOptions(section),
                });
                break;
                // Other object types (NULL, DEFTYPE, DEFSTRUCT) are ignored.
        }
    }

    private static void LoadSubindexSection(
        IniFile ini, IniSection section, Match match, ObjectDictionary od, int? nodeId)
    {
        var index = Convert.ToInt32(match.Groups[1].Value, 16);
        var subindex = Convert.ToInt32(match.Groups[2].Value, 16);
        var entry = od[index];
        var objectType = (int)PythonInt.Parse(section.GetValueOrNull("ObjectType") ?? "0x07");
        var variable = BuildVariable(ini, section, nodeId, objectType, index, subindex);

        // Subindex sections under a plain variable are ignored.
        if (entry is OdComposite composite)
        {
            composite.AddMember(variable);
        }
    }

    private static void LoadNameSection(IniSection section, Match match, ObjectDictionary od)
    {
        var index = Convert.ToInt32(match.Groups[1].Value, 16);
        var count = int.Parse(RequiredOption(section, "NrOfEntries"));
        var array = (OdArray)od[index];
        var template = array[1];

        for (var subindex = 1; subindex <= count; subindex++)
        {
            var name = section.GetValueOrNull(subindex.ToString())
                ?? throw new ParseException($"[{section.Name}] is missing entry {subindex}.");

            array.AddMember(Clone(template, name, subindex));
        }
    }

    private static OdVariable Clone(OdVariable template, string name, int subindex) =>
        new(template.Index, subindex, name)
        {
            DataType = template.DataType,
            AccessType = template.AccessType,
            IsDomain = template.IsDomain,
            PdoMappable = template.PdoMappable,
            Minimum = template.Minimum,
            Maximum = template.Maximum,
            Default = template.Default,
            DefaultRaw = template.DefaultRaw,
            IsRelative = template.IsRelative,
            Value = template.Value,
            ValueRaw = template.ValueRaw,
            Factor = template.Factor,
            Description = template.Description,
            Unit = template.Unit,
            StorageLocation = template.StorageLocation,
            CustomOptions = template.CustomOptions,
        };

    private static OdVariable BuildVariable(
        IniFile ini, IniSection section, int? nodeId, int objectType, int index, int subindex)
    {
        var variable = new OdVariable(index, subindex, RequiredOption(section, "ParameterName"))
        {
            StorageLocation = section.GetValueOrNull("StorageLocation"),
            AccessType = RequiredOption(section, "AccessType").ToLowerInvariant(),
            IsDomain = objectType == 0x02,
        };

        var dataTypeCode = (int)PythonInt.Parse(RequiredOption(section, "DataType"));

        // Codes above UNSIGNED64 are CANFestival-style indirections: the real code
        // sits in [<code>sub1] DefaultValue; without that section the type is
        // treated as DOMAIN.
        if (dataTypeCode > 0x1B)
        {
            dataTypeCode = ini.Find($"{dataTypeCode:X}sub1")?.GetValueOrNull("DefaultValue") is { } real
                ? (int)PythonInt.Parse(real)
                : (int)CanOpenDataType.Domain;
        }

        variable.DataType = (CanOpenDataType)dataTypeCode;

        if (section.GetValueOrNull("PDOMapping") is { } pdoMapping)
        {
            variable.PdoMappable = PythonInt.Parse(pdoMapping) != 0;
        }

        variable.Minimum = ParseLimit(section.GetValueOrNull("LowLimit"), variable.DataType);
        variable.Maximum = ParseLimit(section.GetValueOrNull("HighLimit"), variable.DataType);

        if (section.GetValueOrNull("DefaultValue") is { } defaultValue)
        {
            variable.DefaultRaw = defaultValue;
            variable.IsRelative = defaultValue.Contains("$NODEID");
            variable.Default = DecodeValue(nodeId, variable.DataType, defaultValue);
        }

        if (section.GetValueOrNull("ParameterValue") is { } parameterValue)
        {
            variable.ValueRaw = parameterValue;
            variable.Value = DecodeValue(nodeId, variable.DataType, parameterValue);
        }

        if (section.GetValueOrNull("Factor") is { } factor
            && double.TryParse(factor, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
        {
            variable.Factor = parsed;
        }

        variable.Description = section.GetValueOrNull("Description") ?? "";
        variable.Unit = section.GetValueOrNull("Unit") ?? "";
        variable.CustomOptions = CustomOptions(section);

        return variable;
    }

    private static Dictionary<string, string> CustomOptions(IniSection section)
    {
        var options = new Dictionary<string, string>();

        foreach (var (key, value) in section.Options)
        {
            if (!StandardOptions.Contains(key))
            {
                options[key] = value;
            }
        }

        return options;
    }

    // Invalid limits are skipped, like upstream (which only logs a warning).
    private static long? ParseLimit(string? text, CanOpenDataType dataType)
    {
        if (text is null)
        {
            return null;
        }

        try
        {
            if (dataType.IsSigned())
            {
                return SignedFromEds(text, dataType.BitLength()!.Value);
            }

            return checked((long)PythonInt.Parse(text));
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            return null;
        }
    }

    // Hex limits of signed types are reinterpreted as two's complement: 0xFFFF on
    // INTEGER16 means -1. Out-of-range values are rejected.
    internal static long SignedFromEds(string text, int bitWidth)
    {
        var value = PythonInt.Parse(text);
        var unsignedMax = (Int128.One << bitWidth) - 1;
        var signedMax = (Int128.One << (bitWidth - 1)) - 1;
        var signedMin = -(Int128.One << (bitWidth - 1));

        if (value < signedMin || value > unsignedMax)
        {
            throw new OverflowException($"'{text}' does not fit in {bitWidth} bits.");
        }

        if (value > signedMax)
        {
            value -= Int128.One << bitWidth;
        }

        return (long)value;
    }

    // Decodes a DefaultValue/ParameterValue per data type; null when invalid.
    private static OdValue? DecodeValue(int? nodeId, CanOpenDataType dataType, string text)
    {
        try
        {
            switch (dataType)
            {
                case CanOpenDataType.OctetString or CanOpenDataType.Domain:
                    return Convert.FromHexString(text.Replace(" ", ""));
                case CanOpenDataType.VisibleString or CanOpenDataType.UnicodeString:
                    return text;
                case CanOpenDataType.Real32 or CanOpenDataType.Real64:
                    return double.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
                default:
                    var normalized = text.Replace(" ", "").ToUpperInvariant();

                    if (normalized.Contains("$NODEID"))
                    {
                        if (nodeId is null)
                        {
                            return null;
                        }

                        var rest = NodeIdToken().Replace(normalized, "");

                        return checked((long)PythonInt.Parse(rest) + nodeId.Value);
                    }

                    var number = PythonInt.Parse(normalized);

                    return number >= 0 && number > long.MaxValue
                        ? (ulong)number
                        : checked((long)number);
            }
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    private static string RequiredOption(IniSection section, string key) =>
        section.GetValueOrNull(key)
        ?? throw new ParseException($"[{section.Name}] is missing {key}.");

}
