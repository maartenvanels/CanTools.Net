namespace CanTools.Formats;

/// <summary>
/// Parses integers with Python's <c>int(s, 0)</c> semantics: optional sign, 0x/0o/0b
/// base prefixes, and no leading zeros on decimal numbers.
/// </summary>
internal static class PythonInt
{
    public static Int128 Parse(string text)
    {
        text = text.Trim();
        var negative = false;

        if (text.StartsWith('-') || text.StartsWith('+'))
        {
            negative = text[0] == '-';
            text = text[1..];
        }

        if (text.Length == 0)
        {
            throw new FormatException("Empty number.");
        }

        Int128 magnitude;

        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            magnitude = ParseDigits(text[2..], 16);
        }
        else if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            magnitude = ParseDigits(text[2..], 8);
        }
        else if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            magnitude = ParseDigits(text[2..], 2);
        }
        else
        {
            if (text.Length > 1 && text[0] == '0' && text.TrimStart('0').Length > 0)
            {
                throw new FormatException($"Leading zeros are not allowed: '{text}'.");
            }

            magnitude = ParseDigits(text, 10);
        }

        return negative ? -magnitude : magnitude;
    }

    public static long ParseInt64(string text) => checked((long)Parse(text));

    private static Int128 ParseDigits(string text, int numericBase)
    {
        if (text.Length == 0)
        {
            throw new FormatException("Missing digits.");
        }

        Int128 result = 0;

        foreach (var character in text)
        {
            var digit = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => int.MaxValue,
            };

            if (digit >= numericBase)
            {
                throw new FormatException($"Invalid digit '{character}'.");
            }

            result = checked(result * numericBase + digit);
        }

        return result;
    }
}
