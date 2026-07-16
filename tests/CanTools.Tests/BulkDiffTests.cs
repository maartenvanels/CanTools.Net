using System.Text.Json;
using CanTools.Formats.Dbc;
using CanTools.Model;

namespace CanTools.Tests;

// Temporary bulk differential test against a model dump produced by Python cantools
// (diff-test skill). Skipped automatically when the dump file is absent.
public class BulkDiffTests
{
    private static readonly string DumpPath = Environment.GetEnvironmentVariable("CANTOOLS_MODEL_DUMP")
        ?? "";

    [SkippableFact]
    public void Model_matches_python_cantools()
    {
        Skip.If(DumpPath == "" || !File.Exists(DumpPath), "no python model dump available");

        using var document = JsonDocument.Parse(File.ReadAllText(DumpPath));

        foreach (var fileEntry in document.RootElement.EnumerateObject())
        {
            var parts = fileEntry.Name.Split('/');
            var db = parts[0] switch
            {
                "kcd" => Formats.Kcd.KcdReader.LoadFile(TestFiles.Kcd(parts[1])),
                "sym" => Formats.Sym.SymReader.LoadFile(TestFiles.Sym(parts[1])),
                _ => DbcReader.LoadFile(TestFiles.Dbc(parts[1])),
            };
            var expected = fileEntry.Value;
            var context = fileEntry.Name;

            AssertEqual(expected.GetProperty("version"), db.Version, $"{context}.version");

            var nodes = expected.GetProperty("nodes").EnumerateArray().ToList();
            Assert.Equal(nodes.Count, db.Nodes.Count);
            for (var i = 0; i < nodes.Count; i++)
            {
                AssertEqual(nodes[i].GetProperty("name"), db.Nodes[i].Name, $"{context}.nodes[{i}].name");
                AssertEqual(nodes[i].GetProperty("comment"), db.Nodes[i].Comment, $"{context}.nodes[{i}].comment");
            }

            var buses = expected.GetProperty("buses").EnumerateArray().ToList();
            Assert.Equal(buses.Count, db.Buses.Count);
            for (var i = 0; i < buses.Count; i++)
            {
                AssertEqual(buses[i].GetProperty("name"), db.Buses[i].Name, $"{context}.buses[{i}].name");
                AssertEqual(buses[i].GetProperty("comment"), db.Buses[i].Comment, $"{context}.buses[{i}].comment");
                AssertNumber(buses[i].GetProperty("baudrate"), db.Buses[i].Baudrate, $"{context}.buses[{i}].baudrate");
            }

            var messages = expected.GetProperty("messages").EnumerateArray().ToList();
            Assert.Equal(messages.Count, db.Messages.Count);
            for (var i = 0; i < messages.Count; i++)
            {
                CompareMessage(messages[i], db.Messages[i], $"{context}.messages[{i}]");
            }
        }
    }

    private static void CompareMessage(JsonElement expected, Message message, string context)
    {
        AssertEqual(expected.GetProperty("name"), message.Name, $"{context}.name");
        AssertNumber(expected.GetProperty("frame_id"), message.FrameId, $"{context}.frame_id");
        Assert.Equal(expected.GetProperty("is_extended_frame").GetBoolean(), message.IsExtendedFrame);
        Assert.Equal(expected.GetProperty("is_fd").GetBoolean(), message.IsFd);
        AssertNumber(expected.GetProperty("length"), message.Length, $"{context}.length");
        AssertEqual(expected.GetProperty("protocol"), message.Protocol, $"{context}.protocol");
        AssertEqual(expected.GetProperty("comment"), message.Comment, $"{context}.comment");
        AssertEqual(expected.GetProperty("bus_name"), message.BusName, $"{context}.bus_name");
        AssertNumber(expected.GetProperty("cycle_time"), message.CycleTime, $"{context}.cycle_time");
        AssertEqual(expected.GetProperty("send_type"), message.SendType, $"{context}.send_type");
        Assert.Equal(
            expected.GetProperty("senders").EnumerateArray().Select(e => e.GetString()),
            message.Senders);

        var groups = expected.GetProperty("signal_groups").EnumerateArray().ToList();
        Assert.Equal(groups.Count, message.SignalGroups?.Count ?? 0);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = message.SignalGroups![i];
            AssertEqual(groups[i].GetProperty("name"), group.Name, $"{context}.groups[{i}].name");
            AssertNumber(groups[i].GetProperty("repetitions"), group.Repetitions, $"{context}.groups[{i}].repetitions");
            Assert.Equal(
                groups[i].GetProperty("signal_names").EnumerateArray().Select(e => e.GetString()),
                group.SignalNames);
        }

        var signals = expected.GetProperty("signals").EnumerateArray().ToList();
        Assert.Equal(signals.Count, message.Signals.Count);
        for (var i = 0; i < signals.Count; i++)
        {
            CompareSignal(signals[i], message.Signals[i], $"{context}.signals[{i}]");
        }
    }

    private static void CompareSignal(JsonElement expected, Signal signal, string context)
    {
        AssertEqual(expected.GetProperty("name"), signal.Name, $"{context}.name");
        AssertNumber(expected.GetProperty("start"), signal.StartBit, $"{context}.StartBit");
        AssertNumber(expected.GetProperty("length"), signal.Length, $"{context}.length");
        Assert.Equal(
            expected.GetProperty("byte_order").GetString(),
            signal.ByteOrder == ByteOrder.BigEndian ? "big_endian" : "little_endian");
        Assert.Equal(expected.GetProperty("is_signed").GetBoolean(), signal.IsSigned);
        Assert.Equal(expected.GetProperty("is_float").GetBoolean(), signal.IsFloat);
        AssertNumber(expected.GetProperty("scale"), signal.Scale, $"{context}.scale");
        AssertNumber(expected.GetProperty("offset"), signal.Offset, $"{context}.offset");
        AssertNumber(expected.GetProperty("minimum"), signal.Minimum, $"{context}.minimum");
        AssertNumber(expected.GetProperty("maximum"), signal.Maximum, $"{context}.maximum");
        AssertEqual(expected.GetProperty("unit"), signal.Unit, $"{context}.unit");
        Assert.Equal(
            expected.GetProperty("receivers").EnumerateArray().Select(e => e.GetString()),
            signal.Receivers);
        Assert.Equal(expected.GetProperty("is_multiplexer").GetBoolean(), signal.IsMultiplexer);
        AssertEqual(expected.GetProperty("multiplexer_signal"), signal.MultiplexerSignal, $"{context}.multiplexer_signal");
        AssertNumber(expected.GetProperty("spn"), signal.Spn, $"{context}.spn");
        AssertEqual(expected.GetProperty("comment"), signal.Comment, $"{context}.comment");

        var expectedIds = expected.GetProperty("multiplexer_ids");
        if (expectedIds.ValueKind == JsonValueKind.Null)
        {
            Assert.True(signal.MultiplexerIds is null, $"{context}.multiplexer_ids should be null");
        }
        else
        {
            Assert.Equal(
                expectedIds.EnumerateArray().Select(e => e.GetInt64()),
                signal.MultiplexerIds!.Order());
        }

        var expectedRawInitial = expected.GetProperty("raw_initial");
        if (expectedRawInitial.ValueKind == JsonValueKind.Null)
        {
            Assert.True(signal.RawInitial is null, $"{context}.raw_initial should be null");
        }
        else
        {
            Assert.True(
                Math.Abs(expectedRawInitial.GetDouble() - signal.RawInitial!.Value.ToDouble()) == 0,
                $"{context}.raw_initial");
        }

        var expectedChoices = expected.GetProperty("choices");
        if (expectedChoices.ValueKind == JsonValueKind.Null)
        {
            Assert.True(signal.Choices is null, $"{context}.choices should be null");
        }
        else
        {
            var actual = signal.Choices!;
            Assert.Equal(expectedChoices.EnumerateObject().Count(), actual.Count);

            foreach (var choice in expectedChoices.EnumerateObject())
            {
                var key = long.Parse(choice.Name);
                Assert.True(actual.ContainsKey(key), $"{context}.choices[{key}] missing");
                Assert.Equal(choice.Value.GetProperty("value").GetInt64(), actual[key].Value);
                Assert.Equal(choice.Value.GetProperty("name").GetString(), actual[key].Name);
            }
        }
    }

    private static void AssertEqual(JsonElement expected, string? actual, string context)
    {
        var expectedText = expected.ValueKind == JsonValueKind.Null ? null : expected.GetString();
        Assert.True(expectedText == actual, $"{context}: expected '{expectedText}', got '{actual}'");
    }

    private static void AssertNumber(JsonElement expected, double? actual, string context)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.True(actual is null, $"{context}: expected null, got {actual}");
            return;
        }

        Assert.True(actual is not null && expected.GetDouble() == actual,
            $"{context}: expected {expected.GetDouble()}, got {actual}");
    }
}
