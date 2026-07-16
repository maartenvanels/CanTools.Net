using CanTools.Model;
using Attribute = CanTools.Model.Attribute;

namespace CanTools.Tests;

// The C# counterpart of upstream's Database._differences: dump tests compare the
// reloaded dump against a reloaded golden file semantically, not textually.
internal static class DatabaseComparer
{
    public static void AssertEquivalent(Database expected, Database actual)
    {
        Assert.Equal(expected.Version, actual.Version);

        Assert.Equal(expected.Nodes.Select(n => n.Name), actual.Nodes.Select(n => n.Name));
        Assert.Equal(expected.Nodes.Select(n => n.Comment), actual.Nodes.Select(n => n.Comment));

        Assert.Equal(expected.Buses.Count, actual.Buses.Count);
        for (var i = 0; i < expected.Buses.Count; i++)
        {
            Assert.Equal(expected.Buses[i].Name, actual.Buses[i].Name);
            Assert.Equal(expected.Buses[i].Comment, actual.Buses[i].Comment);
            Assert.Equal(expected.Buses[i].Baudrate, actual.Buses[i].Baudrate);
        }

        Assert.Equal(
            expected.Messages.Select(MessageKey).Order(),
            actual.Messages.Select(MessageKey).Order());

        foreach (var expectedMessage in expected.Messages)
        {
            var actualMessage = actual.Messages.Single(
                message => MessageKey(message) == MessageKey(expectedMessage));
            AssertMessageEquivalent(expectedMessage, actualMessage);
        }

        AssertAttributesEquivalent(
            expected.Dbc?.Attributes, actual.Dbc?.Attributes, "database");
        AssertDefinitionsEquivalent(expected.Dbc, actual.Dbc);
    }

    private static long MessageKey(Message message) =>
        message.FrameId | (message.IsExtendedFrame ? 0x80000000L : 0);

    private static void AssertMessageEquivalent(Message expected, Message actual)
    {
        var context = expected.Name;

        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.IsFd, actual.IsFd);
        Assert.Equal(expected.Senders, actual.Senders);
        Assert.Equal(expected.CycleTime, actual.CycleTime);
        Assert.Equal(expected.SendType, actual.SendType);
        Assert.Equal(expected.Protocol, actual.Protocol);
        Assert.Equal(expected.Comment, actual.Comment);

        var expectedGroups = expected.SignalGroups ?? [];
        var actualGroups = actual.SignalGroups ?? [];
        Assert.Equal(expectedGroups.Count, actualGroups.Count);
        for (var i = 0; i < expectedGroups.Count; i++)
        {
            Assert.Equal(expectedGroups[i].Name, actualGroups[i].Name);
            Assert.Equal(expectedGroups[i].Repetitions, actualGroups[i].Repetitions);
            Assert.Equal(expectedGroups[i].SignalNames, actualGroups[i].SignalNames);
        }

        Assert.Equal(
            expected.Signals.Select(s => s.Name).Order(),
            actual.Signals.Select(s => s.Name).Order());

        foreach (var expectedSignal in expected.Signals)
        {
            AssertSignalEquivalent(
                expectedSignal, actual.GetSignalByName(expectedSignal.Name), context);
        }

        AssertAttributesEquivalent(
            expected.Dbc?.Attributes, actual.Dbc?.Attributes, context);
    }

    private static void AssertSignalEquivalent(Signal expected, Signal actual, string context)
    {
        context = $"{context}.{expected.Name}";

        Assert.Equal(expected.StartBit, actual.StartBit);
        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected.ByteOrder, actual.ByteOrder);
        Assert.Equal(expected.IsSigned, actual.IsSigned);
        Assert.Equal(expected.IsFloat, actual.IsFloat);
        Assert.True(expected.Scale == actual.Scale, $"{context}.scale");
        Assert.True(expected.Offset == actual.Offset, $"{context}.offset");
        Assert.Equal(expected.Minimum, actual.Minimum);
        Assert.Equal(expected.Maximum, actual.Maximum);
        Assert.Equal(expected.Unit, actual.Unit);
        Assert.Equal(expected.Receivers, actual.Receivers);
        Assert.Equal(expected.IsMultiplexer, actual.IsMultiplexer);
        Assert.Equal(expected.MultiplexerSignal, actual.MultiplexerSignal);
        Assert.Equal(expected.Spn, actual.Spn);
        Assert.Equal(expected.Comment, actual.Comment);

        Assert.Equal(
            expected.MultiplexerIds is null, actual.MultiplexerIds is null);
        if (expected.MultiplexerIds is not null)
        {
            Assert.Equal(expected.MultiplexerIds.Order(), actual.MultiplexerIds!.Order());
        }

        Assert.True(
            expected.RawInitial is null
                ? actual.RawInitial is null
                : actual.RawInitial is not null
                  && expected.RawInitial.Value.ToDouble() == actual.RawInitial.Value.ToDouble(),
            $"{context}.raw_initial");

        Assert.Equal(expected.Choices is null, actual.Choices is null);
        if (expected.Choices is not null)
        {
            Assert.Equal(expected.Choices.Count, actual.Choices!.Count);
            foreach (var (value, choice) in expected.Choices)
            {
                Assert.True(actual.Choices.ContainsKey(value), $"{context}.choices[{value}]");
                Assert.Equal(choice.Name, actual.Choices[value].Name);
            }
        }

        AssertAttributesEquivalent(expected.Dbc?.Attributes, actual.Dbc?.Attributes, context);
    }

    private static void AssertAttributesEquivalent(
        IReadOnlyDictionary<string, Attribute>? expected,
        IReadOnlyDictionary<string, Attribute>? actual,
        string context)
    {
        var expectedAttributes = expected ?? new Dictionary<string, Attribute>();
        var actualAttributes = actual ?? new Dictionary<string, Attribute>();

        Assert.True(
            expectedAttributes.Keys.Order(StringComparer.Ordinal)
                .SequenceEqual(actualAttributes.Keys.Order(StringComparer.Ordinal)),
            $"{context}: attribute names differ: "
            + $"[{string.Join(", ", expectedAttributes.Keys)}] vs [{string.Join(", ", actualAttributes.Keys)}]");

        foreach (var (name, attribute) in expectedAttributes)
        {
            Assert.True(
                attribute.Value == actualAttributes[name].Value,
                $"{context}.{name}: {attribute.Value} vs {actualAttributes[name].Value}");
        }
    }

    private static void AssertDefinitionsEquivalent(DbcSpecifics? expected, DbcSpecifics? actual)
    {
        var expectedDefinitions = expected?.AttributeDefinitions
            ?? new Dictionary<string, AttributeDefinition>();
        var actualDefinitions = actual?.AttributeDefinitions
            ?? new Dictionary<string, AttributeDefinition>();

        Assert.True(
            expectedDefinitions.Keys.Order(StringComparer.Ordinal)
                .SequenceEqual(actualDefinitions.Keys.Order(StringComparer.Ordinal)),
            "attribute definitions differ: "
            + $"[{string.Join(", ", expectedDefinitions.Keys)}] vs [{string.Join(", ", actualDefinitions.Keys)}]");

        foreach (var (name, definition) in expectedDefinitions)
        {
            var actualDefinition = actualDefinitions[name];
            Assert.Equal(definition.Kind, actualDefinition.Kind);
            Assert.Equal(definition.TypeName, actualDefinition.TypeName);
            Assert.Equal(definition.Minimum, actualDefinition.Minimum);
            Assert.Equal(definition.Maximum, actualDefinition.Maximum);
            Assert.Equal(definition.Choices, actualDefinition.Choices);
            Assert.True(
                definition.DefaultValue is null
                    ? actualDefinition.DefaultValue is null
                    : actualDefinition.DefaultValue is not null
                      && definition.DefaultValue.Value == actualDefinition.DefaultValue.Value,
                $"definition {name} default: {definition.DefaultValue} vs {actualDefinition.DefaultValue}");
        }
    }
}
