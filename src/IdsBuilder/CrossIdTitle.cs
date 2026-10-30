using System.Globalization;
using System.Text;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// How well a title of the catalog of 3Cat matches the title of a Wikipedia article.
/// </summary>
internal enum TitleMatch
{
    /// <summary>No usable relation between the two titles.</summary>
    None = 0,

    /// <summary>
    /// One title contains the other («Crims» and «Crims a Barcelona»): the article may be the work, but
    /// the title alone does not prove it.
    /// </summary>
    Fuzzy = 1,

    /// <summary>
    /// The article is the title plus a qualifier («Polònia (programa)»), or the two titles only differ in
    /// the article («El detectiu Conan» and «Detectiu Conan»).
    /// </summary>
    BaseTitle = 2,

    /// <summary>The two titles are the same.</summary>
    Exact = 3
}

/// <summary>
/// Compares the titles of the catalog of 3Cat with the titles of Wikipedia articles and Wikidata items.
/// </summary>
/// <remarks>
/// <para>
/// The catalog publishes the Catalan title («Bola de Drac Súper») while the work is known by its original
/// title («Dragon Ball Super»), the Spanish and the Catalan spellings of a country («Alemania» and
/// «Alemanya»), and the articles add a qualifier to the title when the name is ambiguous
/// («Polònia (programa)», «Merlí (sèrie de televisió)»). The comparison therefore ignores the case, the
/// accents, the punctuation and the trailing parenthesis, and ranks the result instead of accepting or
/// rejecting it, so the scorer can weigh the rest of the evidence.
/// </para>
/// <para>
/// The oldest trap of the catalog is the other direction: «Merlí» is the Catalan series, but the Catalan
/// Wikipedia labels four different items «Merlí». Only the identifiers of the item (a TMDB series id, the
/// type of the item) tell them apart, which is what <see cref="MatchScorer"/> does.
/// </para>
/// </remarks>
internal static class CrossIdTitle
{
    /// <summary>Words that a title can drop without changing which work it names.</summary>
    private static readonly HashSet<string> LeadingArticles = new(StringComparer.Ordinal)
    {
        "el", "la", "els", "les", "l", "los", "las", "lo",
        "the", "a", "an",
        "il", "gli", "i"
    };

    /// <summary>
    /// Words a qualifier uses to name a work, already normalized (no accents, lower case, single spaces).
    /// </summary>
    private static readonly string[] WorkQualifiers =
    [
        "programa", "programes", "programa de televisio",
        "serie", "series", "serie de televisio", "series de televisio", "serie de tv", "tv series",
        "miniseria", "miniserie", "telenovela", "anime", "anime series",
        "telefilm", "pel licula", "pelicula", "film", "movie", "llargmetratge", "curtmetratge",
        "documental", "docusserie", "documentary", "reality", "concurs", "quiz show",
        "televisio", "television", "tv", "cinema", "obra de teatre", "theatre", "teatre",
        "novel la", "novel", "manga", "comic", "videojoc", "video game", "podcast", "informatius"
    ];

    /// <summary>
    /// Normalizes a title: lower case, without accents, and with every run of punctuation or spaces
    /// collapsed to a single space.
    /// </summary>
    /// <param name="value">Title.</param>
    /// <returns>The normalized title, or an empty string when there is nothing left.</returns>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return Collapse(builder.ToString());
    }

    /// <summary>
    /// Removes the qualifier an article adds to the title («Polònia (programa)» and «Doraemon (2005)»
    /// become «Polònia» and «Doraemon»).
    /// </summary>
    /// <param name="value">Title of the article.</param>
    /// <returns>The title without the trailing parenthesis.</returns>
    internal static string BaseTitle(string? value)
    {
        var title = value?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            return string.Empty;
        }

        var qualifier = Qualifier(title);
        if (qualifier is null)
        {
            return title;
        }

        var baseTitle = title[..title.LastIndexOf('(')].Trim();

        return baseTitle.Length == 0 ? title : baseTitle;
    }

    /// <summary>
    /// Reads the qualifier an article adds to the title.
    /// </summary>
    /// <param name="title">Title of the article.</param>
    /// <returns>The text between the last parenthesis, or <c>null</c> when the title has none.</returns>
    internal static string? Qualifier(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !trimmed.EndsWith(')'))
        {
            return null;
        }

        var open = trimmed.LastIndexOf('(');

        return open > 0 ? trimmed[(open + 1)..^1].Trim() : null;
    }

    /// <summary>
    /// Tells whether the qualifier of a title names a work («Doraemon (2005 TV series)»,
    /// «Merlí (sèrie de televisió)»).
    /// </summary>
    /// <param name="title">Title of the article.</param>
    /// <returns>Whether the qualifier is a kind of work or a year.</returns>
    /// <remarks>
    /// The qualifier is the opposite of noise: a title that has one is a title that another work shares, and
    /// the qualifier says which of them the article is about. «Polònia» is a country and «Polònia
    /// (programa)» is the programme of 3Cat, so a qualifier that names a work or a year is evidence enough
    /// to be counted by the scorer.
    /// </remarks>
    internal static bool QualifierMentionsWork(string? title)
    {
        var qualifier = Qualifier(title);

        if (string.IsNullOrEmpty(qualifier))
        {
            return false;
        }

        if (QualifierYear(title) is not null)
        {
            return true;
        }

        var normalized = $" {Normalize(qualifier)} ";

        return WorkQualifiers.Any(word => normalized.Contains($" {word} ", StringComparison.Ordinal));
    }


    /// <summary>
    /// Compares a title of the catalog with the title of an article.
    /// </summary>
    /// <param name="candidate">Title of the catalog.</param>
    /// <param name="articleTitle">Title of the article.</param>
    /// <returns>How well the two titles match.</returns>
    internal static TitleMatch Compare(string? candidate, string? articleTitle)
    {
        var left = Normalize(candidate);
        var right = Normalize(articleTitle);

        if (left.Length < 2 || right.Length < 2)
        {
            return TitleMatch.None;
        }

        if (left == right)
        {
            return TitleMatch.Exact;
        }

        // «La capitana Marleau» and «Capitana Marleau» are the same title: the article is not part of the name
        // of the work, and the catalog drops it as often as the Wikipedia keeps it.
        if (WithoutArticle(left) == WithoutArticle(right))
        {
            return TitleMatch.Exact;
        }

        var baseRight = Normalize(BaseTitle(articleTitle));
        if (baseRight.Length >= 2
            && (left == baseRight || WithoutArticle(left) == WithoutArticle(baseRight)))
        {
            return TitleMatch.BaseTitle;
        }

        return Contains(left, right) || Contains(right, left)
            ? TitleMatch.Fuzzy
            : TitleMatch.None;
    }

    /// <summary>
    /// Reads the year a title publishes between parentheses («Doraemon (2005)» is 2005).
    /// </summary>
    /// <param name="title">Title of the article.</param>
    /// <returns>The year, or <c>null</c> when the qualifier is not a year.</returns>
    internal static int? QualifierYear(string? title)
    {
        var qualifier = Qualifier(title);

        return int.TryParse(qualifier, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
            && year is > 1900 and < 2100
                ? year
                : null;
    }

    private static bool Contains(string shorter, string longer)
        => shorter.Length >= 4
            && longer.Length > shorter.Length
            && $" {longer} ".Contains($" {shorter} ", StringComparison.Ordinal);

    private static string WithoutArticle(string normalized)
    {
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return words.Length > 1 && LeadingArticles.Contains(words[0])
            ? string.Join(' ', words[1..])
            : normalized;
    }

    private static string Collapse(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSpace = true;

        foreach (var character in value)
        {
            if (character == ' ')
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }
            }
            else
            {
                builder.Append(character);
            }

            lastWasSpace = character == ' ';
        }

        return builder.ToString().Trim();
    }
}
