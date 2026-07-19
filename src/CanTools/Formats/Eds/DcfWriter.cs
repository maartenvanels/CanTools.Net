using System.Globalization;
using CanTools.CanOpen;

namespace CanTools.Formats.Eds;

/// <summary>How the node id is written in the [DeviceComissioning] section.</summary>
public enum NodeIdFormat
{
    /// <summary>Hexadecimal, e.g. <c>0x0A</c> (the default).</summary>
    Hex,

    /// <summary>Decimal, e.g. <c>10</c>.</summary>
    Decimal,
}

/// <summary>Options for <see cref="DcfWriter"/>.</summary>
public sealed class DcfWriterOptions
{
    /// <summary>How to write the node id. Defaults to <see cref="NodeIdFormat.Hex"/>.</summary>
    public NodeIdFormat NodeIdFormat { get; set; } = NodeIdFormat.Hex;
}

/// <summary>
/// Writes an <see cref="ObjectDictionary"/> that was loaded from an EDS/DCF back to
/// a DCF, preserving the source file's structure, comments and ordering and layering
/// in the commissioning data (node id, bitrate) and the configured
/// <c>ParameterValue</c>s. This is the load → modify → write workflow; building a
/// dictionary from scratch is not supported.
/// </summary>
public static class DcfWriter
{
    public static string WriteString(ObjectDictionary dictionary, DcfWriterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        options ??= new DcfWriterOptions();

        var source = dictionary.SourceDocument ?? throw new InvalidOperationException(
            "Only an object dictionary loaded from an EDS/DCF file can be written as a "
            + "DCF; building one from scratch is not supported.");
        var document = source.Clone();

        if (dictionary.NodeId is { } nodeId)
        {
            document.UpsertInSection("DeviceComissioning", "NodeID", FormatNodeId(nodeId, options.NodeIdFormat));
        }

        if (dictionary.Bitrate is { } bitrate)
        {
            document.UpsertInSection(
                "DeviceComissioning", "Baudrate",
                (bitrate / 1000).ToString(CultureInfo.InvariantCulture));
        }

        foreach (var entry in dictionary.Entries)
        {
            switch (entry)
            {
                case OdVariable variable:
                    WriteParameterValue(document, variable, subindex: null);
                    break;
                case OdComposite composite:
                    foreach (var member in composite.Members)
                    {
                        WriteParameterValue(document, member, subindex: member.Subindex);
                    }

                    break;
            }
        }

        return document.ToString();
    }

    public static void WriteFile(
        ObjectDictionary dictionary, string path, DcfWriterOptions? options = null) =>
        File.WriteAllText(path, WriteString(dictionary, options));

    private static void WriteParameterValue(IniDocument document, OdVariable variable, int? subindex)
    {
        if (variable.Value is not { } value)
        {
            return;
        }

        // Values read from the file are echoed verbatim so $NODEID expressions and hex
        // notation survive; only values set through the API are formatted afresh.
        var text = variable.ValueIsOverridden || variable.ValueRaw is null
            ? EdsValueFormatter.Format(value, variable.DataType)
            : variable.ValueRaw;

        if (!document.TryUpsertObject(variable.Index, subindex, "ParameterValue", text))
        {
            throw new NotSupportedException(
                $"Cannot write ParameterValue for 0x{variable.Index:X4}sub{variable.Subindex:X}: "
                + "it has no section in the source file (a synthesized compact-array member).");
        }
    }

    private static string FormatNodeId(int nodeId, NodeIdFormat format) => format == NodeIdFormat.Hex
        ? $"0x{nodeId:X2}"
        : nodeId.ToString(CultureInfo.InvariantCulture);
}
