using System.Text;

namespace CanTools.Cli;

/// <summary>Resolves the -e/--encoding option, accepting Python-style names like "cp1252".</summary>
internal static class TextEncodings
{
    static TextEncodings()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Resolve(string name)
    {
        if (name.StartsWith("cp", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name[2..], out var codePage))
        {
            return Encoding.GetEncoding(codePage);
        }

        return Encoding.GetEncoding(name);
    }
}
