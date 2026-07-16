using System.Text;

namespace CanTools.Formats;

/// <summary>
/// Default text encodings of the database formats: cp1252 for DBC and SYM, UTF-8
/// otherwise, matching upstream cantools.
/// </summary>
internal static class FormatEncodings
{
    static FormatEncodings()
    {
        // cp1252 needs the code-pages provider on .NET.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Cp1252 =>
        Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback,
                             new DecoderReplacementFallback("�"));
}
