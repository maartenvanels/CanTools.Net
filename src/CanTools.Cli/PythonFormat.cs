using System.Globalization;

namespace CanTools.Cli;

/// <summary>
/// Formats doubles the way Python's str()/repr() would ("2.0", "0.0001", "1.5e-05"),
/// because the reference output of every subcommand comes from the Python CLI.
/// </summary>
internal static class PythonFormat
{
    /// <summary>
    /// Formats a number that upstream keeps as Python int-or-float but the C# model
    /// stores as a double: integral values print as integers ("250"), everything else
    /// as repr ("0.01"). Deviates from upstream for integral Python floats, which
    /// would print as "250.0"; the model does not retain that distinction.
    /// </summary>
    public static string Number(double value) =>
        double.IsFinite(value) && Math.Floor(value) == value && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : Repr(value);

    public static string Repr(double value)
    {
        if (double.IsNaN(value))
        {
            return "nan";
        }

        if (double.IsInfinity(value))
        {
            return value > 0 ? "inf" : "-inf";
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var negative = text.StartsWith('-');

        if (negative)
        {
            text = text[1..];
        }

        var (digits, exponent) = Decompose(text);

        if (digits.Length == 0)
        {
            return negative ? "-0.0" : "0.0";
        }

        // Python switches to scientific notation below 1e-4 and at 1e16.
        var formatted = exponent is >= -4 and < 16
            ? Plain(digits, exponent)
            : Scientific(digits, exponent);

        return negative ? "-" + formatted : formatted;
    }

    /// <summary>
    /// Splits a round-trip formatted double into its significant digits (without
    /// trailing zeros) and the base-10 exponent of the leading digit.
    /// </summary>
    private static (string Digits, int Exponent) Decompose(string text)
    {
        var e = text.IndexOf('E');
        var mantissa = e < 0 ? text : text[..e];
        var dot = mantissa.IndexOf('.');
        var integral = dot < 0 ? mantissa : mantissa[..dot];
        var fraction = dot < 0 ? "" : mantissa[(dot + 1)..];

        var digits = (integral + fraction).TrimStart('0');
        var leadingZeros = integral.Length + fraction.Length - digits.Length;
        var exponent = integral.Length - 1 - leadingZeros;

        if (e >= 0)
        {
            exponent += int.Parse(text[(e + 1)..], CultureInfo.InvariantCulture);
        }

        return (digits.TrimEnd('0'), exponent);
    }

    private static string Plain(string digits, int exponent)
    {
        if (exponent < 0)
        {
            return "0." + new string('0', -exponent - 1) + digits;
        }

        var integral = digits.Length > exponent
            ? digits[..(exponent + 1)]
            : digits.PadRight(exponent + 1, '0');
        var fraction = digits.Length > exponent + 1 ? digits[(exponent + 1)..] : "0";

        return integral + "." + fraction;
    }

    private static string Scientific(string digits, int exponent)
    {
        var mantissa = digits.Length > 1 ? digits[0] + "." + digits[1..] : digits;
        var sign = exponent < 0 ? '-' : '+';

        return $"{mantissa}e{sign}{Math.Abs(exponent):00}";
    }
}
