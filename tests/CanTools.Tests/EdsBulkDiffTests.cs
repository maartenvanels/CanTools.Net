using System.Text.Json;
using CanTools.CanOpen;
using CanTools.Formats.Eds;

namespace CanTools.Tests;

// Differential test against an object dictionary dump produced by python-canopen
// (diff-test skill). Skipped when the dump file is absent.
public class EdsBulkDiffTests
{
    private static readonly string DumpPath =
        Environment.GetEnvironmentVariable("CANOPEN_OD_DUMP") ?? "";

    [SkippableFact]
    public void Object_dictionary_matches_python_canopen()
    {
        Skip.If(DumpPath == "" || !File.Exists(DumpPath), "no python od dump available");

        using var document = JsonDocument.Parse(File.ReadAllText(DumpPath));

        foreach (var fileEntry in document.RootElement.EnumerateObject())
        {
            var parts = fileEntry.Name.Split('#');
            int? nodeId = parts[1] == "None" ? null : int.Parse(parts[1]);
            var od = EdsReader.LoadFile(TestFiles.Eds(parts[0]), nodeId);
            var expected = fileEntry.Value;

            Assert.Equal(AsInt(expected.GetProperty("node_id")), od.NodeId);
            Assert.Equal(AsInt(expected.GetProperty("bitrate")), od.Bitrate);
            Assert.Equal(expected.GetProperty("comments").GetString(), od.Comments);

            var entries = expected.GetProperty("entries").EnumerateArray().ToList();
            Assert.Equal(entries.Count, od.Entries.Count);

            foreach (var (expectedEntry, actual) in entries.Zip(od.Entries))
            {
                CompareEntry(expectedEntry, actual, fileEntry.Name);
            }
        }
    }

    private static void CompareEntry(JsonElement expected, OdEntry actual, string context)
    {
        context = $"{context} 0x{actual.Index:X4}";
        Assert.Equal(expected.GetProperty("index").GetInt32(), actual.Index);
        Assert.Equal(expected.GetProperty("name").GetString(), actual.Name);

        switch (expected.GetProperty("kind").GetString())
        {
            case "var":
                CompareVariable(expected, Assert.IsType<OdVariable>(actual), context);
                break;
            case "array":
                var array = Assert.IsType<OdArray>(actual);
                CompareMembers(expected, array.Members, context);
                break;
            default:
                var record = Assert.IsType<OdRecord>(actual);
                CompareMembers(expected, record.Members, context);
                break;
        }
    }

    private static void CompareMembers(
        JsonElement expected, IReadOnlyCollection<OdVariable> members, string context)
    {
        var expectedMembers = expected.GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(expectedMembers.Count, members.Count);

        foreach (var (expectedMember, actual) in expectedMembers.Zip(members))
        {
            CompareVariable(expectedMember, actual, context);
        }
    }

    private static void CompareVariable(JsonElement expected, OdVariable actual, string context)
    {
        context = $"{context}sub{actual.Subindex}";
        Assert.Equal(expected.GetProperty("subindex").GetInt32(), actual.Subindex);
        Assert.Equal(expected.GetProperty("name").GetString(), actual.Name);
        Assert.Equal(expected.GetProperty("data_type").GetInt32(), (int)actual.DataType);
        Assert.Equal(expected.GetProperty("access_type").GetString(), actual.AccessType);
        Assert.Equal(expected.GetProperty("is_domain").GetBoolean(), actual.IsDomain);
        Assert.Equal(expected.GetProperty("pdo_mappable").GetBoolean(), actual.PdoMappable);
        Assert.Equal(AsLong(expected.GetProperty("min")), actual.Minimum);
        Assert.Equal(AsLong(expected.GetProperty("max")), actual.Maximum);
        Assert.Equal(
            expected.GetProperty("default_raw").ValueKind == JsonValueKind.Null
                ? null
                : expected.GetProperty("default_raw").GetString(),
            actual.DefaultRaw);
        Assert.Equal(expected.GetProperty("relative").GetBoolean(), actual.IsRelative);
        Assert.Equal(expected.GetProperty("factor").GetDouble(), actual.Factor);
        Assert.Equal(expected.GetProperty("description").GetString(), actual.Description);
        Assert.Equal(expected.GetProperty("unit").GetString(), actual.Unit);

        var expectedDefault = expected.GetProperty("default");
        if (expectedDefault.ValueKind == JsonValueKind.Null)
        {
            Assert.True(actual.Default is null, $"{context}: default should be null");
        }
        else
        {
            OdValue expectedValue = expectedDefault.ValueKind switch
            {
                JsonValueKind.String => expectedDefault.GetString()!,
                JsonValueKind.Object => Convert.FromHexString(
                    expectedDefault.GetProperty("bytes").GetString()!),
                _ when expectedDefault.TryGetInt64(out var integer) => integer,
                _ => expectedDefault.GetDouble(),
            };
            Assert.True(
                actual.Default is not null && expectedValue == actual.Default.Value,
                $"{context}: default {expectedDefault} vs {actual.Default}");
        }

        var options = expected.GetProperty("custom_options");
        Assert.Equal(options.EnumerateObject().Count(), actual.CustomOptions.Count);
        foreach (var option in options.EnumerateObject())
        {
            Assert.Equal(option.Value.GetString(), actual.CustomOptions[option.Name]);
        }
    }

    private static int? AsInt(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt32();

    private static long? AsLong(JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt64();
}
