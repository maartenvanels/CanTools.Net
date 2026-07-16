namespace CanTools.Tests;

// Equality semantics mirror Python: numbers compare by value across int/float,
// NamedSignalValue compares equal to its label but never to a number.
public class SignalValueTests
{
    [Fact]
    public void Numeric_values_compare_by_value()
    {
        Assert.True((SignalValue)1 == 1.0);
        Assert.True((SignalValue)(-5L) == -5.0);
        Assert.True((SignalValue)ulong.MaxValue == ulong.MaxValue);
        Assert.False((SignalValue)1 == 2);
        Assert.False((SignalValue)(-1L) == ulong.MaxValue);
    }

    [Fact]
    public void Named_values_compare_by_label()
    {
        SignalValue named = new NamedSignalValue(1, "High");

        Assert.True(named == "High");
        Assert.False(named == "Low");
        Assert.True(named == new NamedSignalValue(1, "High"));
        Assert.False(named == new NamedSignalValue(2, "High"));
    }

    [Fact]
    public void Numbers_and_labels_are_never_equal()
    {
        SignalValue named = new NamedSignalValue(1, "High");

        Assert.False(named == 1);
        Assert.False((SignalValue)"1" == 1);
    }

    [Fact]
    public void Non_numeric_values_reject_numeric_access()
    {
        SignalValue label = "High";

        Assert.Throws<InvalidOperationException>(() => label.ToDouble());
    }
}
