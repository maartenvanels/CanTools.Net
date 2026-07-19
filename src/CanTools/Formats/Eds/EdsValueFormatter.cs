using System.Globalization;
using CanTools.CanOpen;

namespace CanTools.Formats.Eds;

/// <summary>
/// Formats an <see cref="OdValue"/> as the text of an EDS/DCF DefaultValue or
/// ParameterValue, the inverse of <c>EdsReader.DecodeValue</c>: decimal integers
/// (negatives included), invariant-culture reals, verbatim strings and hex for
/// octet strings and domains.
/// </summary>
internal static class EdsValueFormatter
{
    public static string Format(OdValue value, CanOpenDataType type)
    {
        switch (type)
        {
            case CanOpenDataType.VisibleString or CanOpenDataType.UnicodeString:
                return value.Text ?? throw new EncodeException(
                    "A string value is required for a string data type.");

            case CanOpenDataType.OctetString or CanOpenDataType.Domain:
                return Convert.ToHexString(value.Bytes ?? throw new EncodeException(
                    "A byte value is required for an octet string or domain."));

            case CanOpenDataType.Real32 or CanOpenDataType.Real64:
                return value.ToDouble().ToString(CultureInfo.InvariantCulture);

            default:
                return type.IsSigned()
                    ? value.ToInt64().ToString(CultureInfo.InvariantCulture)
                    : value.ToUInt64().ToString(CultureInfo.InvariantCulture);
        }
    }
}
