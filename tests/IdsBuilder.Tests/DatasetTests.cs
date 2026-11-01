using System.Text.Json;
using OpenTvIds.IdsBuilder;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of the files of the dataset: the record of a programme, the JSON dialect it is written with, the
/// manual corrections and the review file.
/// </summary>
public class DatasetTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Fact]
    public void TheRecordCarriesTheIdentifiersAndWhereTheyComeFrom()
    {
        var record = SeriesIdRecord.Create(
            "3cat",
            Program(),
            Article("Dragon Ball Super", "en", "Dragon Ball Super", "Q19839121", TitleMatch.Exact),
            Facts("Q19839121", imdb: "tt4644488", tmdb: "62715", tvdb: "295068"),
            new MatchVerdict(0.90, MatchState.Accepted, "título exacto"),
            Today);

        Assert.Equal("3cat:73476", record.Id);
        Assert.Equal("3cat", record.Emitter);
        Assert.Equal("73476", record.EmitterId);
        Assert.Equal("anime", record.Kind);
        Assert.Equal("Dragon Ball Super", record.OriginalTitle);
        Assert.Equal(new DateOnly(2025, 6, 6), record.AvailableSince);
        Assert.Equal(4, record.Ids.Count);
        Assert.Equal("Q19839121", record.Ids["wikidata"]);
        Assert.Equal("tt4644488", record.Ids["imdb"]);
        Assert.Equal("62715", record.Ids["tmdb"]);
        Assert.Equal("295068", record.Ids["tvdb"]);
        Assert.Contains("wikidata:Q19839121", record.Sources);
        Assert.Contains("wikipedia:en:Dragon Ball Super", record.Sources);
        Assert.Equal(Today, record.FirstSeen);
    }

    [Fact]
    public void TheRecordDropsAnIdentifierThatIsNotOne()
    {
        var record = SeriesIdRecord.Create(
            "3cat",
            Program(),
            Article("Dragon Ball Super", "en", "Dragon Ball Super", "Q19839121", TitleMatch.Exact),
            Facts("Q19839121", imdb: "Q123", tmdb: "62715", tvdb: "abc"),
            new MatchVerdict(0.90, MatchState.Accepted, "título exacto"),
            Today);

        Assert.Equal("62715", record.Ids["tmdb"]);
        Assert.False(record.Ids.ContainsKey("imdb"));
        Assert.False(record.Ids.ContainsKey("tvdb"));
    }

    [Fact]
    public void TheRecordIsWrittenWithCamelCaseAndWithTheAccentsOfTheCatalog()
    {
        var record = SeriesIdRecord.Create(
            "3cat",
            Program(),
            Article("Bola de Drac Súper", "ca", "Bola de Drac Súper", "Q19839121", TitleMatch.Exact),
            Facts("Q19839121", imdb: "tt4644488", tmdb: "62715", tvdb: "295068"),
            new MatchVerdict(0.90, MatchState.Accepted, "título exacto"),
            Today);

        var json = JsonSerializer.Serialize(record, DatasetJson.Options);

        Assert.Contains("\"emitterId\":\"73476\"", json, StringComparison.Ordinal);
        Assert.Contains("\"availableSince\":\"2025-06-06\"", json, StringComparison.Ordinal);
        Assert.Contains("\"verified\":false", json, StringComparison.Ordinal);
        Assert.Contains("Súper", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u00", json, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordSurvivesARoundTrip()
    {
        var record = SeriesIdRecord.Create(
            "3cat",
            Program(),
            Article("Dragon Ball Super", "en", "Dragon Ball Super", "Q19839121", TitleMatch.Exact),
            Facts("Q19839121", imdb: "tt4644488", tmdb: "62715", tvdb: "295068"),
            new MatchVerdict(0.90, MatchState.Accepted, "título exacto"),
            Today);

        var copy = JsonSerializer.Deserialize<SeriesIdRecord>(
            JsonSerializer.Serialize(record, DatasetJson.Options),
            DatasetJson.Options);

        Assert.NotNull(copy);
        Assert.Equal(record.Id, copy.Id);
        Assert.Equal(record.Title, copy.Title);
        Assert.Equal(record.Ids, copy.Ids);
        Assert.Equal(record.FirstSeen, copy.FirstSeen);
        Assert.Equal(record.Match.Confidence, copy.Match.Confidence);
    }

    [Fact]
    public void AManualCorrectionWinsOverTheMatcher()
    {
        var record = SeriesIdRecord.Create(
            "3cat",
            Program(),
            null,
            null,
            new MatchVerdict(0, MatchState.Unmatched, "sin artículo"),
            Today);

        Overrides.Apply(record, new ManualOverride
        {
            Id = "3cat:73476",
            Ids = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tmdb"] = "62715",
                ["imdb"] = "tt4644488",
                ["wikidata"] = "not-a-qid"
            },
            Note = "Confirmado en la ficha de TMDB",
            Verified = true
        });

        Assert.Equal("62715", record.Ids["tmdb"]);
        Assert.Equal("tt4644488", record.Ids["imdb"]);
        Assert.False(record.Ids.ContainsKey("wikidata"));
        Assert.Equal("manual", record.Match.Method);
        Assert.Equal(1, record.Match.Confidence);
        Assert.True(record.Match.Verified);
        Assert.Contains("manual", record.Sources);
    }

    [Fact]
    public void TheOverridesOfTheFileAreReadAndABrokenFileDoesNotStopTheBuild()
    {
        var good = Path.Combine(Path.GetTempPath(), $"3cat-overrides-{Guid.NewGuid():N}.json");
        var broken = Path.Combine(Path.GetTempPath(), $"3cat-overrides-{Guid.NewGuid():N}.json");
        File.WriteAllText(good, """[{"id":"3cat:1","ids":{"tmdb":"42"},"verified":true}]""");
        File.WriteAllText(broken, "[");

        try
        {
            var overrides = Overrides.Read(good);

            var manual = Assert.Single(overrides).Value;
            Assert.Equal("3cat:1", manual.Id);
            Assert.Equal("42", manual.Ids["tmdb"]);
            Assert.True(manual.Verified);

            Assert.Empty(Overrides.Read(broken));
            Assert.Empty(Overrides.Read(Path.Combine(Path.GetTempPath(), "no-existe.json")));
        }
        finally
        {
            File.Delete(good);
            File.Delete(broken);
        }
    }

    [Fact]
    public void TheWriterPublishesOneLinePerRecordAndAReviewFileWithEveryProgramme()
    {
        var article = Article("Dragon Ball Super", "en", "Dragon Ball Super", "Q19839121", TitleMatch.Exact);
        var facts = Facts("Q19839121", imdb: "tt4644488", tmdb: "62715", tvdb: "295068");
        var verdict = new MatchVerdict(0.90, MatchState.Accepted, "título exacto");
        var record = SeriesIdRecord.Create("3cat", Program(), article, facts, verdict, Today);

        var dropped = new Outcome(
            new ProgramEntry { Id = "1", Title = "Gent amb DO", Category = "Documental", IsTelevision = true },
            null,
            new ArticleHit("ca", "SX3", "Q8347160", 0),
            TitleMatch.None,
            null,
            new MatchVerdict(0, MatchState.Unmatched, "sin artículo"),
            null);

        var folder = Path.Combine(Path.GetTempPath(), $"3cat-{Guid.NewGuid():N}");

        try
        {
            DatasetWriter.WriteDataset(Path.Combine(folder, "data", "3cat", "series.jsonl"), [record]);
            DatasetWriter.WriteReview(Path.Combine(folder, "report", "3cat", "revision.csv"), "3cat", new BuildResult(
                [new Outcome(Program(), article, null, TitleMatch.None, facts, verdict, null), dropped],
                [record],
                [],
                TimeSpan.FromSeconds(3)));

            var lines = File.ReadAllLines(Path.Combine(folder, "data", "3cat", "series.jsonl"));
            Assert.Single(lines);
            Assert.StartsWith("{\"id\":\"3cat:73476\"", lines[0], StringComparison.Ordinal);

            var review = File.ReadAllLines(Path.Combine(folder, "report", "3cat", "revision.csv"));
            Assert.Equal(3, review.Length);
            Assert.Contains("\"publicado\"", review[1], StringComparison.Ordinal);
            Assert.Contains("\"sin_match\"", review[2], StringComparison.Ordinal);
            Assert.Contains("Gent amb DO", review[2], StringComparison.Ordinal);
            Assert.Contains("SX3", review[2], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheRecordOfAnIdentifierOfWikidataSaysSoAndKeepsThePageOfTheProgramme()
    {
        // No article: the item comes from the identifier of the page that Wikidata states (P10356), which is
        // the case of a programme without a Wikipedia article.
        var record = SeriesIdRecord.Create(
            "3cat",
            Program(),
            article: null,
            new EntityFacts { Qid = "Q69652877", Imdb = "tt14670820", Tmdb = "123591", Tvdb = "75719" },
            MatchScorer.FromIdentifier("P10356", "els-barrufets-3d"),
            Today,
            SeriesMatchInfo.IdentifierMethod);

        record.Sources.Add("wikidata:P10356:els-barrufets-3d");

        Assert.Equal("wikidata-id", record.Match.Method);
        Assert.Equal(1, record.Match.Confidence);
        Assert.Equal("Q69652877", record.Ids["wikidata"]);
        Assert.Equal("tt14670820", record.Ids["imdb"]);
        Assert.Equal(
            new[] { "wikidata:Q69652877", "wikidata:P10356:els-barrufets-3d" },
            record.Sources);

        var json = JsonSerializer.Serialize(record, DatasetJson.Options);

        Assert.Contains("\"method\":\"wikidata-id\"", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"sources\":[\"wikidata:Q69652877\",\"wikidata:P10356:els-barrufets-3d\"]",
            json,
            StringComparison.Ordinal);
    }

    private static ProgramEntry Program()
        => new()
        {
            Id = "73476",
            Title = "Bola de Drac Súper",
            DetailTitle = "Bola de Drac Súper",
            OriginalTitle = "Dragon Ball Super",
            Category = "Anime",
            AvailableSince = "2025-06-06 07:00:00",
            IsTelevision = true
        };

    private static ArticleMatch Article(string searched, string language, string title, string qid, TitleMatch match)
        => new(new ArticleHit(language, title, qid, 0), match, searched);

    private static EntityFacts Facts(string qid, string? imdb, string? tmdb, string? tvdb)
        => new() { Qid = qid, Imdb = imdb, Tmdb = tmdb, Tvdb = tvdb, StartYear = 2015, IsSeries = true };
}
