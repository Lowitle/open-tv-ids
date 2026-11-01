using OpenTvIds.IdsBuilder;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of <see cref="MatchScorer"/> with the cases the catalog has already put in front of the matcher.
/// </summary>
/// <remarks>
/// The expected values are the ones a build of the catalog produced with these very programmes, so the tests
/// fail if the weights or the thresholds drift away from what the dataset publishes.
/// </remarks>
public class MatchScorerTests
{
    [Fact]
    public void ExactTitleWithTmdbAndSeriesAndYearIsPublished()
    {
        var verdict = MatchScorer.Evaluate(
            Program("Polònia", "Humor", "2006-02-16 00:00:00", originalTitle: null),
            Article("Polònia", "ca", "Polònia", "Q2688944", TitleMatch.Exact),
            Facts("Q2688944", imdb: "tt0764959", tmdb: "74497", tvdb: "280244", startYear: 2006, series: true));

        Assert.Equal(MatchState.Accepted, verdict.State);
        Assert.Equal(0.99, verdict.Confidence);
    }

    [Fact]
    public void QualifiedArticleWithWorkQualifierIsPublished()
    {
        var verdict = MatchScorer.Evaluate(
            Program("Merlí", "Sèrie", "2015-09-14 21:55:08", originalTitle: null),
            Article("Merlí", "ca", "Merlí (sèrie de televisió)", "Q21036200", TitleMatch.BaseTitle),
            Facts("Q21036200", imdb: "tt4580372", tmdb: "62914", tvdb: "300428", startYear: 2015, series: true));

        Assert.Equal(MatchState.Accepted, verdict.State);
        Assert.Equal(0.99, verdict.Confidence);
    }

    [Fact]
    public void YearOfTheCatalogThatDoesNotMatchDoesNotPenalise()
    {
        // 3Cat dates «Bola de Drac Súper» in 2025 because that is when the licence arrived; the series
        // started in 2015, and the difference must not cost anything.
        var verdict = MatchScorer.Evaluate(
            Program("Bola de Drac Súper", "Anime", "2025-06-06 07:00:00", "Dragon Ball Super"),
            Article("Bola de Drac Súper", "ca", "Bola de Drac Súper", "Q19839121", TitleMatch.Exact),
            Facts("Q19839121", imdb: "tt4644488", tmdb: "62715", tvdb: "295068", startYear: 2015, series: true));

        Assert.Equal(MatchState.Accepted, verdict.State);
        Assert.Equal(0.90, verdict.Confidence);
    }

    [Fact]
    public void QualifiedArticleOfTheOriginalTitleIsPublished()
    {
        var verdict = MatchScorer.Evaluate(
            Program("Doraemon, el gat còsmic", "Anime", "2013-04-18 19:44:51", "Doraemon"),
            Article("Doraemon", "en", "Doraemon (2005 TV series)", "Q1136431", TitleMatch.BaseTitle),
            Facts("Q1136431", imdb: null, tmdb: "103470", tvdb: "80650", startYear: 2005, series: true));

        Assert.Equal(MatchState.Accepted, verdict.State);
        Assert.Equal(0.90, verdict.Confidence);
    }

    [Fact]
    public void ItemWithoutIdentifiersIsNotPublished()
    {
        // The Catalan article of «Bola de Drac» is the franchise: it is a work, but it publishes no TMDB or
        // TVDB id that the plugin could use, so the record stays out of the dataset.
        var verdict = MatchScorer.Evaluate(
            Program("Bola de Drac", "Anime", "1990-03-16 00:00:00", "Dragon Ball"),
            Article("Bola de Drac", "ca", "Bola de Drac", "Q2020", TitleMatch.Exact),
            Facts("Q2020", imdb: null, tmdb: null, tvdb: null, startYear: 1984, series: false));

        Assert.Equal(MatchState.Unmatched, verdict.State);
        Assert.Equal(0.60, verdict.Confidence);
    }

    [Fact]
    public void FuzzyTitleWithTmdbIsLeftForReview()
    {
        var verdict = MatchScorer.Evaluate(
            Program("Crims al Bàltic", "Sèrie", null, null),
            Article("Crims al Bàltic", "ca", "Crims al Bàltic: Ombres nocturnes", "Q104156981", TitleMatch.Fuzzy),
            Facts("Q104156981", imdb: null, tmdb: "434016", tvdb: null, startYear: 2016, series: false));

        Assert.Equal(MatchState.Unmatched, verdict.State);
        Assert.Equal(0.60, verdict.Confidence);
    }

    [Fact]
    public void IdentifiersThatAreNotIdentifiersAreNotCounted()
    {
        var verdict = MatchScorer.Evaluate(
            Program("Naruto", "Anime", "2023-12-07 20:50:56", null),
            Article("Naruto", "ca", "Naruto", "Q26971382", TitleMatch.Exact),
            Facts("Q26971382", imdb: "Q123", tmdb: "naruto", tvdb: "-4", startYear: 1999, series: false));

        Assert.Equal(MatchState.Unmatched, verdict.State);
        Assert.Equal(0.60, verdict.Confidence);
    }

    [Fact]
    public void WithoutArticleThereIsNothingToPublish()
    {
        var verdict = MatchScorer.Evaluate(
            Program("Gent amb DO", "Documental", "2024-01-01 00:00:00", null),
            null,
            null);

        Assert.Equal(MatchState.Unmatched, verdict.State);
        Assert.Equal(0, verdict.Confidence);
    }

    [Fact]
    public void TheThresholdsOfTheCommandLineAreHonoured()
    {
        // An exact title with only an IMDb id is worth 0,70: the default review threshold publishes it in the
        // review file and the default accept threshold keeps it out of the dataset.
        var program = Program("In vitro", "Documental", "2022-01-01 00:00:00", null);
        var article = Article("In vitro", "ca", "In vitro", "Q1", TitleMatch.Exact);
        var facts = Facts("Q1", imdb: "tt1234567", tmdb: null, tvdb: null, startYear: 2020, series: false);

        var byDefault = MatchScorer.Evaluate(program, article, facts);
        Assert.Equal(MatchState.Review, byDefault.State);
        Assert.Equal(0.70, byDefault.Confidence);

        Assert.Equal(MatchState.Accepted, MatchScorer.Evaluate(program, article, facts, accept: 0.70).State);
        Assert.Equal(MatchState.Unmatched, MatchScorer.Evaluate(program, article, facts, review: 0.75).State);
    }

    [Fact]
    public void FuzzyTitleWithEverythingButTheTitleIsLeftForReview()
    {
        // A film of a series and the series itself share the title: the type, the identifiers and the year
        // take a fuzzy title to 0,85, and 0,90 is where the dataset starts publishing.
        var verdict = MatchScorer.Evaluate(
            Program("Doraemon, el gat còsmic", "Anime", "2013-04-18 19:44:51", "Doraemon"),
            Article("Doraemon", "ca", "Doraemon (sèrie de televisió)", "Q1136431", TitleMatch.Fuzzy),
            Facts("Q1136431", imdb: null, tmdb: "103470", tvdb: "80650", startYear: 2013, series: true));

        Assert.Equal(MatchState.Review, verdict.State);
        Assert.Equal(0.85, verdict.Confidence);
    }

    [Fact]
    public void TheEvidenceNamesWhatTheScoreIsBuiltFrom()
    {
        var verdict = MatchScorer.Evaluate(
            Program("Polònia", "Humor", "2006-02-16 00:00:00", null),
            Article("Polònia", "ca", "Polònia (programa)", "Q2688944", TitleMatch.BaseTitle),
            Facts("Q2688944", imdb: "tt0764959", tmdb: "74497", tvdb: "280244", startYear: 2006, series: true));

        Assert.Contains("título con calificador", verdict.Evidence, StringComparison.Ordinal);
        Assert.Contains("«programa»", verdict.Evidence, StringComparison.Ordinal);
        Assert.Contains("TMDB+TVDB", verdict.Evidence, StringComparison.Ordinal);
        Assert.Contains("serie de televisión", verdict.Evidence, StringComparison.Ordinal);
        Assert.Contains("año 2006", verdict.Evidence, StringComparison.Ordinal);
    }

    [Fact]
    public void AnIdentifierOfWikidataNeedsNoTitleAndPublishesWithFullConfidence()
    {
        // «Els barrufets 3D» and «Els barrufets» are two series with the same title: the identifier of the page
        // in Wikidata (P10356) is the only evidence that tells them apart, and it is not a guess.
        var verdict = MatchScorer.FromIdentifier("P10356", "els-barrufets-3d");

        Assert.Equal(MatchState.Accepted, verdict.State);
        Assert.Equal(1, verdict.Confidence);
        Assert.Contains("P10356", verdict.Evidence, StringComparison.Ordinal);
        Assert.Contains("els-barrufets-3d", verdict.Evidence, StringComparison.Ordinal);
    }

    private static ProgramEntry Program(string title, string category, string? availableSince, string? originalTitle)
        => new()
        {
            Id = "1",
            Title = title,
            DetailTitle = title,
            OriginalTitle = originalTitle,
            Category = category,
            AvailableSince = availableSince,
            IsTelevision = true
        };

    private static ArticleMatch Article(string searched, string language, string title, string qid, TitleMatch match)
        => new(new ArticleHit(language, title, qid, 0), match, searched);

    private static EntityFacts Facts(string qid, string? imdb, string? tmdb, string? tvdb, int? startYear, bool series)
        => new()
        {
            Qid = qid,
            Imdb = imdb,
            Tmdb = tmdb,
            Tvdb = tvdb,
            StartYear = startYear,
            IsSeries = series
        };
}
