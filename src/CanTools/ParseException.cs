namespace CanTools;

/// <summary>Thrown when a database file cannot be parsed.</summary>
public class ParseException : CanToolsException
{
    public ParseException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates a parse error pointing at <paramref name="offset"/> in
    /// <paramref name="text"/>, formatted like upstream cantools:
    /// <c>Invalid syntax at line 1, column 9: "CM_ BO_ &gt;&gt;!&lt;&lt;"Foo.";"</c>.
    /// </summary>
    public static ParseException AtOffset(string text, int offset)
    {
        var line = 1;
        var lineStart = 0;

        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        var lineEnd = text.IndexOf('\n', lineStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var lineText = text[lineStart..lineEnd].TrimEnd('\r');
        var column = Math.Min(offset, lineEnd) - lineStart + 1;
        var marked = lineText.Insert(Math.Min(column - 1, lineText.Length), ">>!<<");

        return new ParseException(
            $"Invalid syntax at line {line}, column {column}: \"{marked}\"");
    }
}
