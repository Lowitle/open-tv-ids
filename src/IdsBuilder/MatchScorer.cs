namespace OpenTvIds.IdsBuilder;

/// <summary>
/// What the builder does with a programme.
/// </summary>
internal enum MatchState
{
    /// <summary>The identifiers are published in the dataset.</summary>
    Accepted = 0,

    /// <summary>The evidence is not enough to publish, but a person can confirm it in the review file.</summary>
    Review = 1,

    /// <summary>No article, or an article that is clearly another work.</summary>
    Unmatched = 2
}

/// <summary>
/// The verdict of the matcher for a programme: what it decided and why.
/// </summary>
/// <param name="Confidence">Confidence in the match, from 0 to 0,99 (1 is reserved for a manual check).</param>
/// <param name="State">What the builder does with the programme.</param>
/// <param name="Evidence">The pieces of evidence the confidence is built from, for the review file.</param>
internal sealed record MatchVerdict(double Confidence, MatchState State, string Evidence);

/// <summary>
/// Decides whether the Wikidata item found for a programme is the work of the programme, and with how
/// much confidence.
/// </summary>
/// <remarks>
/// <para>
/// The score is a sum of independent pieces of evidence, capped at 0,99 because the dataset only marks a
/// record as verified when a second source confirms it:
/// </para>
/// <list type="bullet">
/// <item><description>the title of the catalog and the title of the article: 0,40 to 0,60;</description></item>
/// <item><description>a qualifier that names a work or a year («Polònia (programa)», «Doraemon (2005 TV series)»): 0,05;</description></item>
/// <item><description>a TMDB or TVDB id, which are identifiers of audiovisual works: 0,20;</description></item>
/// <item><description>only an IMDb id, which a person, a film and a series share: 0,10;</description></item>
/// <item><description>the item is a television series (or a subclass): 0,10;</description></item>
/// <item><description>the start year of the item agrees with the catalog: 0,10.</description></item>
/// </list>
/// <para>
/// A year that does not agree costs nothing: the date of the catalog is the date the programme became
/// available on 3Cat, not the date the work premiered («Bola de Drac Súper» is filed under 2025 and the
/// series is from 2015), so it can only confirm a match, never discard it.
/// </para>
/// </remarks>
internal static class MatchScorer
{
    /// <summary>Confidence from which a record is published.</summary>
    internal const double DefaultAccept = 0.90;

    /// <summary>Confidence from which a record is worth a manual check.</summary>
    internal const double DefaultReview = 0.70;

    /// <summary>
    /// Evaluates the evidence of a programme.
    /// </summary>
    /// <param name="program">The programme of the catalog.</param>
    /// <param name="article">The article chosen for it, or <c>null</c> when no article matched.</param>
    /// <param name="facts">The item of the article, or <c>null</c> when it could not be read.</param>
    /// <param name="accept">Confidence from which the record is published.</param>
    /// <param name="review">Confidence from which the record is worth a manual check.</param>
    /// <returns>The verdict.</returns>
    internal static MatchVerdict Evaluate(
        ProgramEntry program,
        ArticleMatch? article,
        EntityFacts? facts,
        double accept = DefaultAccept,
        double review = DefaultReview)
    {
        if (article is null)
        {
            return new MatchVerdict(0, MatchState.Unmatched, "sin artículo");
        }

        var evidence = new List<string>(4);
        var score = article.Match switch
        {
            TitleMatch.Exact => 0.60,
            TitleMatch.BaseTitle => 0.55,
            TitleMatch.Fuzzy => 0.40,
            _ => 0.0
        };

        evidence.Add(article.Match switch
        {
            TitleMatch.Exact => "título exacto",
            TitleMatch.BaseTitle => "título con calificador",
            TitleMatch.Fuzzy => "título parecido",
            _ => "título distinto"
        });

        if (CrossIdTitle.QualifierMentionsWork(article.Hit.Title))
        {
            score += 0.05;
            evidence.Add($"calificador «{CrossIdTitle.Qualifier(article.Hit.Title)}»");
        }

        if (facts is not null)
        {
            var tmdb = ExternalIds.Tmdb(facts.AnyTmdb);
            var tvdb = ExternalIds.Tvdb(facts.Tvdb);
            var imdb = ExternalIds.Imdb(facts.Imdb);

            if (tmdb is not null || tvdb is not null)
            {
                score += 0.20;
                evidence.Add(tmdb is not null && tvdb is not null ? "TMDB+TVDB" : tmdb is not null ? "TMDB" : "TVDB");
            }
            else if (imdb is not null)
            {
                score += 0.10;
                evidence.Add("IMDb");
            }

            if (facts.IsSeries)
            {
                score += 0.10;
                evidence.Add("serie de televisión");
            }

            if (program.Year is { } year && facts.StartYear is { } start && Math.Abs(year - start) <= 1)
            {
                score += 0.10;
                evidence.Add($"año {start}");
            }
        }

        var confidence = Math.Min(0.99, Math.Round(score, 2, MidpointRounding.AwayFromZero));
        var state = confidence >= accept
            ? MatchState.Accepted
            : confidence >= review
                ? MatchState.Review
                : MatchState.Unmatched;

        return new MatchVerdict(confidence, state, string.Join(", ", evidence));
    }
}
