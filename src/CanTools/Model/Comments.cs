namespace CanTools.Model;

/// <summary>
/// Descriptions of a database object, optionally in multiple languages. A plain string
/// becomes the default (language-less) comment; formats like ARXML add per-language
/// entries such as "EN" or "FOR-ALL".
/// </summary>
public sealed class Comments
{
    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>();

    public Comments(string defaultComment)
    {
        Default = defaultComment ?? throw new ArgumentNullException(nameof(defaultComment));
        ByLanguage = Empty;
    }

    public Comments(IReadOnlyDictionary<string, string> byLanguage, string? defaultComment = null)
    {
        Default = defaultComment;
        ByLanguage = byLanguage ?? throw new ArgumentNullException(nameof(byLanguage));
    }

    /// <summary>The comment without a language, when present.</summary>
    public string? Default { get; }

    /// <summary>Comments indexed by language code.</summary>
    public IReadOnlyDictionary<string, string> ByLanguage { get; }

    /// <summary>
    /// The most suitable single comment: the default one, then "FOR-ALL", then "EN".
    /// </summary>
    public string? Resolve()
    {
        if (Default is not null)
        {
            return Default;
        }

        if (ByLanguage.TryGetValue("FOR-ALL", out var forAll))
        {
            return forAll;
        }

        return ByLanguage.TryGetValue("EN", out var english) ? english : null;
    }

    public static implicit operator Comments(string comment) => new(comment);

    public override string ToString() => Resolve() ?? "";
}
