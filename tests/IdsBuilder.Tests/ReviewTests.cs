using OpenTvIds.IdsBuilder;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of the manual review: the file a person reads, the answers it understands, the corrections they
/// produce and the session that asks for them.
/// </summary>
public class ReviewTests
{
    private static readonly DateOnly Today = new(2026, 10, 27);

    [Fact]
    public void TheReviewFileCarriesTheLinksAndTheIdentifiersACandidateWouldPublish()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"3cat-review-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "revision.csv");

        try
        {
            DatasetWriter.WriteReview(path, "3cat", new BuildResult(
                [Reviewing()],
                [],
                [],
                TimeSpan.FromSeconds(3)));

            var lines = File.ReadAllLines(path);

            Assert.Contains("\"revisado\"", lines[0], StringComparison.Ordinal);
            Assert.Contains("\"url_3cat\"", lines[0], StringComparison.Ordinal);
            Assert.Contains("\"url_wikidata\"", lines[0], StringComparison.Ordinal);
            Assert.Contains("\"url_suggerencia\"", lines[0], StringComparison.Ordinal);

            Assert.StartsWith("\"73476\";\"no\";", lines[1], StringComparison.Ordinal);
            Assert.Contains("\"https://www.3cat.cat/3cat/bola-de-drac-super/\"", lines[1], StringComparison.Ordinal);
            Assert.Contains("\"https://www.wikidata.org/wiki/Q19839121\"", lines[1], StringComparison.Ordinal);
            Assert.Contains("\"tt4644488\"", lines[1], StringComparison.Ordinal);
            Assert.Contains("\"62715\"", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void TheSheetReadsTheReviewFileTheBuildWrote()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"3cat-review-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "revision.csv");

        try
        {
            DatasetWriter.WriteReview(path, "3cat", new BuildResult(
                [Reviewing()],
                [],
                [],
                TimeSpan.FromSeconds(3)));

            var row = Assert.Single(RevisionSheet.Load(path, "3cat"));

            Assert.Equal("3cat:73476", row.Id);
            Assert.Equal("73476", row.EmitterId);
            Assert.Equal("revisar", row.State);
            Assert.Equal(0.8, row.Confidence);
            Assert.Equal("Bola de Drac Súper", row.Title);
            Assert.Equal("Q19839121", row.Wikidata);
            Assert.Equal("tt4644488", row.Imdb);
            Assert.Equal("https://www.3cat.cat/3cat/bola-de-drac-super/", row.ProgramUrl);
            Assert.True(row.NeedsReview);
            Assert.False(row.Unmatched);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void TheColumnOfThePageIsNamedAfterTheEmitterItReports()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"rtve-review-{Guid.NewGuid():N}");
        var path = Path.Combine(folder, "revision.csv");

        try
        {
            DatasetWriter.WriteReview(path, "rtve", new BuildResult(
                [Reviewing()],
                [],
                [],
                TimeSpan.FromSeconds(3)));

            var header = File.ReadAllLines(path)[0];

            Assert.Contains("\"url_rtve\"", header, StringComparison.Ordinal);
            Assert.DoesNotContain("\"url_3cat\"", header, StringComparison.Ordinal);
            Assert.Equal(
                "https://www.3cat.cat/3cat/bola-de-drac-super/",
                Assert.Single(RevisionSheet.Load(path, "rtve")).ProgramUrl);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("d", "Skip")]
    [InlineData("s", "Accept")]
    [InlineData("sí", "Accept")]
    [InlineData("V", "Verify")]
    [InlineData("n", "Reject")]
    [InlineData("q", "Quit")]
    [InlineData("?", "Help")]
    [InlineData("Q123", "Other")]
    [InlineData("o Q123", "Other")]
    [InlineData("https://www.wikidata.org/wiki/Q2688944", "Other")]
    [InlineData("ni idea", "Unknown")]
    public void TheReviewUnderstandsTheAnswer(string line, string expected)
        => Assert.Equal(expected, ReviewAnswers.Parse(line).Action.ToString());

    [Fact]
    public void TheReviewReadsTheItemOfAWorkFromAnIdOrFromItsPage()
    {
        Assert.Equal("Q2688944", ReviewAnswers.Parse("https://www.wikidata.org/wiki/Q2688944").Value);
        Assert.Equal("Q123", ReviewAnswers.Parse("q123").Value);
        Assert.Null(ReviewAnswers.Parse("o").Value);
        Assert.Null(ReviewAnswers.Item("Polònia"));
    }

    [Fact]
    public void AcceptingARowPublishesEveryIdentifierItShows()
    {
        var manual = ReviewDecision.Accept(
            Row("3cat:73476", 0.8, "Q19839121", "tt4644488"),
            verified: true);

        Assert.Equal("3cat:73476", manual.Id);
        Assert.Equal("Q19839121", manual.Ids["wikidata"]);
        Assert.Equal("tt4644488", manual.Ids["imdb"]);
        Assert.True(manual.Verified);
        Assert.False(manual.Reject);
    }

    [Fact]
    public void ACorrectionReplacesTheIdentifiersOfTheMatcher()
    {
        var record = SeriesIdRecord.Create(
            "3cat",
            Program(),
            Article("Bola de Drac Súper", "ca", "Bola de Drac Súper", "Q19839121", TitleMatch.Exact),
            Facts("Q19839121", "tt4644488", "62715", "295068"),
            new MatchVerdict(0.90, MatchState.Accepted, "título exacto"),
            Today);

        Assert.Equal(4, record.Ids.Count);

        Overrides.Apply(record, new ManualOverride
        {
            Id = "3cat:73476",
            Ids = new Dictionary<string, string>(StringComparer.Ordinal) { ["wikidata"] = "Q2688944" },
            Verified = true
        });

        Assert.Equal("Q2688944", Assert.Single(record.Ids).Value);
        Assert.True(record.Match.Verified);
    }

    [Fact]
    public void ARejectedProgrammeIsKeptOutOfTheDataset()
    {
        var article = Article("Bola de Drac Súper", "ca", "Bola de Drac Súper", "Q19839121", TitleMatch.Exact);
        var facts = Facts("Q19839121", "tt4644488", "62715", "295068");
        var verdict = new MatchVerdict(0.80, MatchState.Review, "título exacto");
        var record = SeriesIdRecord.Create("3cat", Program(), article, facts, verdict, Today);
        var manual = ReviewDecision.Reject(Row("3cat:73476", 0.8, "Q19839121", "tt4644488"));

        Overrides.Apply(record, manual);
        Assert.Empty(record.Ids);

        var outcome = new Outcome(Program(), article, null, TitleMatch.None, facts, verdict, manual);

        Assert.True(manual.Reject);
        Assert.Equal("descartado", DatasetWriter.State(outcome));
        Assert.Null(DatasetWriter.Identifier(outcome, null, "wikidata"));
        Assert.Null(DatasetWriter.Identifier(outcome, null, "imdb"));
    }

    [Fact]
    public void AProgrammeUnderReviewShowsTheIdentifiersOfTheCandidate()
    {
        var outcome = Reviewing();

        Assert.Equal("revisar", DatasetWriter.State(outcome));
        Assert.Equal("Q19839121", DatasetWriter.Identifier(outcome, null, "wikidata"));
        Assert.Equal("tt4644488", DatasetWriter.Identifier(outcome, null, "imdb"));
        Assert.Equal("62715", DatasetWriter.Identifier(outcome, null, "tmdb"));
        Assert.Equal("295068", DatasetWriter.Identifier(outcome, null, "tvdb"));
    }

    [Fact]
    public async Task TheSessionSavesADecisionAsSoonAsItIsGiven()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"3cat-check-{Guid.NewGuid():N}");
        var options = new Options { Emitter = "3cat", OutputDir = folder };

        try
        {
            var rows = new List<RevisionRow>
            {
                Row("3cat:1", 0.85, "Q19839121", "tt4644488"),
                Row("3cat:2", 0.80, "Q2688944", null)
            };

            var session = new ReviewSession(options, new StringReader("s\nn\nq\n"), TextWriter.Null, null);

            Assert.Equal(0, await session.RunAsync(rows, CancellationToken.None));

            var overrides = Overrides.Read(options.OverridesPath);

            Assert.Equal(2, overrides.Count);
            Assert.Equal("Q19839121", overrides["3cat:1"].Ids["wikidata"]);
            Assert.Equal("tt4644488", overrides["3cat:1"].Ids["imdb"]);
            Assert.False(overrides["3cat:1"].Verified);
            Assert.False(overrides["3cat:1"].Reject);
            Assert.True(overrides["3cat:2"].Reject);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TheSessionSkipsWhatIsAlreadyDecided()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"3cat-check-{Guid.NewGuid():N}");
        var options = new Options { Emitter = "3cat", OutputDir = folder };

        try
        {
            var first = new ReviewSession(options, new StringReader("s\n"), TextWriter.Null, null);
            var rows = new List<RevisionRow> { Row("3cat:1", 0.85, "Q19839121", null) };

            Assert.Equal(0, await first.RunAsync(rows, CancellationToken.None));

            var second = new ReviewSession(options, new StringReader("n\n"), TextWriter.Null, null);

            Assert.Equal(0, await second.RunAsync(rows, CancellationToken.None));
            Assert.False(Overrides.Read(options.OverridesPath)["3cat:1"].Reject);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    private static Outcome Reviewing()
        => new(
            Program(),
            Article("Bola de Drac Súper", "ca", "Bola de Drac Súper", "Q19839121", TitleMatch.Exact),
            null,
            TitleMatch.None,
            Facts("Q19839121", "tt4644488", "62715", "295068"),
            new MatchVerdict(0.80, MatchState.Review, "título exacto"),
            null);

    private static RevisionRow Row(string id, double confidence, string? wikidata, string? imdb)
        => new()
        {
            Id = id,
            EmitterId = id[(id.IndexOf(':') + 1)..],
            Title = "Bola de Drac Súper",
            Category = "Anime",
            Kind = "anime",
            State = "revisar",
            Confidence = confidence,
            Wikidata = wikidata,
            Imdb = imdb
        };

    private static ProgramEntry Program()
        => new()
        {
            Id = "73476",
            Title = "Bola de Drac Súper",
            DetailTitle = "Bola de Drac Súper",
            OriginalTitle = "Dragon Ball Super",
            Category = "Anime",
            AvailableSince = "2025-06-06 07:00:00",
            Url = "https://www.3cat.cat/3cat/bola-de-drac-super/",
            IsTelevision = true
        };

    private static ArticleMatch Article(string searched, string language, string title, string qid, TitleMatch match)
        => new(new ArticleHit(language, title, qid, 0), match, searched);

    private static EntityFacts Facts(string qid, string? imdb, string? tmdb, string? tvdb)
        => new() { Qid = qid, Imdb = imdb, Tmdb = tmdb, Tvdb = tvdb, StartYear = 2015, IsSeries = true };
}
