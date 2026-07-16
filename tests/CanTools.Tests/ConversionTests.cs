namespace CanTools.Tests;

// Ported from tests/test_conversion.py.
public class ConversionTests
{
    private static IReadOnlyDictionary<long, NamedSignalValue> Choices(params (long Value, string Name)[] entries) =>
        entries.ToDictionary(e => e.Value, e => new NamedSignalValue(e.Value, e.Name));

    // ported from test_conversion.py::test_base_conversion_factory
    [Fact]
    public void Create_picks_the_most_precise_implementation()
    {
        var conversion = Conversion.Create();
        Assert.IsType<IdentityConversion>(conversion);
        Assert.Equal(1, conversion.Scale);
        Assert.Equal(0, conversion.Offset);
        Assert.False(conversion.IsFloat);
        Assert.Null(conversion.Choices);

        conversion = Conversion.Create(scale: 2, offset: 3);
        Assert.IsType<LinearIntegerConversion>(conversion);
        Assert.Equal(2, conversion.Scale);
        Assert.Equal(3, conversion.Offset);
        Assert.False(conversion.IsFloat);
        Assert.Null(conversion.Choices);

        conversion = Conversion.Create(scale: 2.5, offset: -1.5, isFloat: true);
        Assert.IsType<LinearConversion>(conversion);
        Assert.Equal(2.5, conversion.Scale);
        Assert.Equal(-1.5, conversion.Offset);
        Assert.True(conversion.IsFloat);
        Assert.Null(conversion.Choices);

        conversion = Conversion.Create(choices: Choices((0, "Off"), (1, "On")));
        Assert.IsType<NamedSignalConversion>(conversion);
        Assert.Equal(1, conversion.Scale);
        Assert.Equal(0, conversion.Offset);
        Assert.False(conversion.IsFloat);
        Assert.NotNull(conversion.Choices);
        Assert.Equal("Off", conversion.Choices[0].Name);
        Assert.Equal("On", conversion.Choices[1].Name);
    }

    // ported from test_conversion.py::test_identity_conversion
    [Fact]
    public void Identity_conversion_returns_values_unchanged()
    {
        var conversion = Conversion.Create(isFloat: true);

        Assert.Equal(1, conversion.Scale);
        Assert.Equal(0, conversion.Offset);
        Assert.True(conversion.IsFloat);

        Assert.Equal((SignalValue)42, conversion.RawToScaled(42));
        Assert.Equal((SignalValue)42, conversion.ScaledToRaw(42));
    }

    // ported from test_conversion.py::test_linear_integer_conversion
    [Fact]
    public void Linear_integer_conversion_scales_exactly()
    {
        var conversion = Conversion.Create(scale: 2, offset: 10);

        Assert.Equal((SignalValue)10, conversion.RawToScaled(0));
        Assert.Equal((SignalValue)12, conversion.RawToScaled(1));
        Assert.Equal((SignalValue)14, conversion.RawToScaled(2));

        Assert.Equal((SignalValue)0, conversion.ScaledToRaw(10));
        Assert.Equal((SignalValue)1, conversion.ScaledToRaw(12));
        Assert.Equal((SignalValue)2, conversion.ScaledToRaw(14));
    }

    // ported from test_conversion.py::test_linear_conversion
    [Fact]
    public void Linear_conversion_scales_with_floating_point()
    {
        var conversion = Conversion.Create(scale: 1.5, offset: 10, isFloat: true);

        Assert.Equal((SignalValue)10.0, conversion.RawToScaled(0.0));
        Assert.Equal((SignalValue)11.5, conversion.RawToScaled(1.0));
        Assert.Equal((SignalValue)13.0, conversion.RawToScaled(2.0));

        Assert.Equal((SignalValue)0.0, conversion.ScaledToRaw(10));
        Assert.Equal((SignalValue)1.0, conversion.ScaledToRaw(11.5));
        Assert.Equal((SignalValue)2.0, conversion.ScaledToRaw(13));
    }
}

// Ported from tests/test_conversion.py::TestNamedSignalConversion.
public class NamedSignalConversionTests
{
    private readonly Conversion _conversion = Conversion.Create(
        scale: 2.0,
        offset: -1.0,
        choices: new Dictionary<long, NamedSignalValue>
        {
            [0] = new(0, "Low"),
            [1] = new(1, "High"),
        });

    // ported from test_conversion.py::test_raw_to_scaled
    [Fact]
    public void Raw_value_without_choice_is_scaled()
    {
        Assert.Equal((SignalValue)9.0, _conversion.RawToScaled(5));
    }

    // ported from test_conversion.py::test_raw_to_scaled_with_choice
    [Fact]
    public void Raw_value_with_choice_decodes_to_the_named_value()
    {
        Assert.Equal((SignalValue)new NamedSignalValue(1, "High"), _conversion.RawToScaled(1));
    }

    // ported from test_conversion.py::test_raw_to_scaled_without_choice
    [Fact]
    public void Choice_decoding_can_be_disabled()
    {
        Assert.Equal((SignalValue)1.0, _conversion.RawToScaled(1, decodeChoices: false));
    }

    // ported from test_conversion.py::test_scaled_to_raw_with_choice
    [Fact]
    public void Named_value_encodes_to_its_raw_value()
    {
        Assert.Equal((SignalValue)1, _conversion.ScaledToRaw(new NamedSignalValue(1, "High")));
    }

    // ported from test_conversion.py::test_scaled_to_raw_without_choice
    [Fact]
    public void Numeric_value_encodes_through_the_linear_conversion()
    {
        Assert.Equal((SignalValue)1, _conversion.ScaledToRaw(1.0));
    }

    // ported from test_conversion.py::test_choice_to_number
    [Fact]
    public void Label_maps_to_its_raw_value()
    {
        Assert.Equal(1, _conversion.ChoiceToNumber("High"));
    }

    // ported from test_conversion.py::test_choice_to_number_with_invalid_choice
    [Fact]
    public void Unknown_label_throws()
    {
        Assert.Throws<KeyNotFoundException>(() => _conversion.ChoiceToNumber("Invalid"));
    }
}
