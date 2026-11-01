using OpenTvIds.IdsBuilder;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of the sheet that Mix'n'match imports: which programmes it lists, which ones it already matches, how
/// it names them and how the file is written.
/// </summary>
public class MixNMatchTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);

    [Fact]
    public void TheSheetListsEveryProgrammeWithTheSlugOfItsPage()
    {
        var catalog = MixNMatchSheet.Create("3cat", Result());

        Assert.Equal(3, catalog.Entries);
        Assert.Equal(
            new[] { "els-barrufets-3d", "polonia", "merli" },
            catalog.Rows.Select(row => row.Id));
        Assert.Equal("Els barrufets 3D", catalog.Rows[0].Name);
        Assert.Equal("https://www.3cat.cat/3cat/els-barrufets-3d/", catalog.Rows[0].Url);
    }

    [Fact]
    public void TheIdentifierOfWikidataIsACertaintyAndATitleIsAGuess()
    {
        var catalog = MixNMatchSheet.Create("3cat", Result());

        // La ficha la declara Wikidata: no hay nada que confirmar.
        Assert.Equal("Q69652877", catalog.Rows[0].Certain);
        Assert.Null(catalog.Rows[0].Guess);

        // La encontró una búsqueda por título: se queda como apuesta.
        Assert.Null(catalog.Rows[1].Certain);
        Assert.Equal("Q1278095", catalog.Rows[1].Guess);

        // Sin coincidencia: la fila va vacía para que la case alguien.
        Assert.Null(catalog.Rows[2].Certain);
        Assert.Null(catalog.Rows[2].Guess);

        Assert.Equal(1, catalog.Certain);
        Assert.Equal(1, catalog.Guess);
        Assert.Equal(1, catalog.Pending);
    }

    [Fact]
    public void ACorrectionOfAPersonIsACertainty()
    {
        var program = Program("61234", "Merlí", "merli", "Sèrie", null, Url("merli"));
        var record = SeriesIdRecord.Create(
            "3cat",
            program,
            null,
            new EntityFacts { Qid = "Q1544337" },
            new MatchVerdict(1, MatchState.Accepted, "corrección a mano"),
            Today);

        record.Match.Method = SeriesMatchInfo.ManualMethod;

        var catalog = MixNMatchSheet.Create("3cat", new BuildResult(
            [Unmatched(program)],
            [record],
            [],
            TimeSpan.Zero));

        Assert.Equal("Q1544337", Assert.Single(catalog.Rows).Certain);
    }

    [Fact]
    public void AProgrammeDismissedByHandIsNotInTheSheet()
    {
        var program = Program("61234", "Merlí", "merli", "Sèrie", null, Url("merli"));

        var catalog = MixNMatchSheet.Create("3cat", new BuildResult(
            [
                Unmatched(program) with
                {
                    Manual = new ManualOverride { Id = "3cat:61234", Reject = true }
                }
            ],
            [],
            [],
            TimeSpan.Zero));

        Assert.Empty(catalog.Rows);
        Assert.Equal(0, catalog.Entries);
    }

    [Fact]
    public void TwoProgrammesThatPublishTheSamePageShareOneRow()
    {
        var first = Program("60336", "Polònia", "polonia", "Humor", null, Url("polonia"));
        var second = Program("60337", "Polònia (resum)", "polonia", "Humor", null, Url("polonia"));
        var log = new List<string>();

        var catalog = MixNMatchSheet.Create(
            "3cat",
            new BuildResult(
                [Unmatched(first), Unmatched(second)],
                [],
                [],
                TimeSpan.Zero),
            log.Add);

        Assert.Equal("Polònia", Assert.Single(catalog.Rows).Name);
        Assert.Equal(1, catalog.Repeated);
        Assert.Contains(log, message => message.Contains("polonia", StringComparison.Ordinal));
    }

    [Fact]
    public void AProgrammeWithoutAPageIsCountedAndLeftOut()
    {
        var catalog = MixNMatchSheet.Create(
            "3cat",
            Single(new ProgramEntry { Id = "1", Title = "Matins", IsTelevision = true }));

        Assert.Empty(catalog.Rows);
        Assert.Equal(1, catalog.WithoutPage);
    }

    [Fact]
    public void TheAddressOfThePageIsEnoughWhenTheDumpPublishesNoSlug()
    {
        var program = Program("61234", "Merlí", slug: string.Empty, "Sèrie", null, Url("merli"));

        Assert.Equal("merli", Assert.Single(MixNMatchSheet.Create("3cat", Single(program)).Rows).Id);
    }

    [Fact]
    public void TheDescriptionSaysTheKindOfWorkAndTheOriginalTitle()
    {
        var byOriginal = Program("73476", "Bola de Drac Súper", "bola-de-drac-super", "Anime", "Dragon Ball Super", Url("bola-de-drac-super"));
        var sameTitle = Program("60336", "Polònia", "polonia", "Humor", "Polònia", Url("polonia"));
        var wide = Program("1", "Un títol", "un-titol", "Sèrie", new string('a', 300), Url("un-titol"));

        var catalog = MixNMatchSheet.Create("3cat", new BuildResult(
            [.. new[] { byOriginal, sameTitle, wide }.Select(Unmatched)],
            [],
            [],
            TimeSpan.Zero));

        Assert.Equal("Anime · Dragon Ball Super", catalog.Rows[0].Description);
        Assert.Equal("Humor", catalog.Rows[1].Description);
        Assert.Equal(255, catalog.Rows[2].Description.Length);
    }

    [Fact]
    public void TheFileIsTabSeparatedWithoutTheMarkAndWithoutBrokenRows()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"mixnmatch-{Guid.NewGuid():N}");

        try
        {
            var path = Path.Combine(folder, "mixnmatch", "3cat.tsv");
            MixNMatchSheet.Write(path, MixNMatchSheet.Create("3cat", Result()));

            var bytes = File.ReadAllBytes(path);
            var lines = File.ReadAllLines(path);

            // La marca de orden de bytes acabaría dentro del nombre de la primera columna.
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.Equal("id\tname\tdesc\turl\tq\tautoq", lines[0]);
            Assert.Equal(4, lines.Length);
            Assert.All(lines, line => Assert.Equal(6, line.Split('\t').Length));
            Assert.Contains("Q69652877", lines[1], StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ATabInsideATitleDoesNotBreakTheRow()
    {
        var program = Program("1", "Merlí\t(sèrie)", "merli", "Sèrie", null, Url("merli"));
        var folder = Path.Combine(Path.GetTempPath(), $"mixnmatch-{Guid.NewGuid():N}");

        try
        {
            var path = Path.Combine(folder, "3cat.tsv");
            MixNMatchSheet.Write(path, MixNMatchSheet.Create("3cat", Single(program)));

            var line = File.ReadAllLines(path)[1];

            Assert.Equal(6, line.Split('\t').Length);
            Assert.Contains("Merlí (sèrie)", line, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static BuildResult Result()
    {
        var brull = Program("73476", "Els barrufets 3D", "els-barrufets-3d", "Animació", null, Url("els-barrufets-3d"));
        var polonia = Program("60336", "Polònia", "polonia", "Humor", null, Url("polonia"));
        var merli = Program("61234", "Merlí", "merli", "Sèrie", null, Url("merli"));

        var brullFacts = new EntityFacts { Qid = "Q69652877", Imdb = "tt14670820", Tmdb = "123591", Tvdb = "75719" };
        var poloniaFacts = new EntityFacts { Qid = "Q1278095", Tmdb = "3421", IsSeries = true };

        var brullVerdict = MatchScorer.FromIdentifier("P10356", "els-barrufets-3d");
        var poloniaVerdict = new MatchVerdict(0.90, MatchState.Accepted, "título exacto");
        var merliVerdict = new MatchVerdict(0.40, MatchState.Unmatched, "título parcial");
        var poloniaArticle = new ArticleMatch(
            new ArticleHit("ca", "Polònia", "Q1278095", 0),
            TitleMatch.Exact,
            "Polònia");

        return new BuildResult(
            [
                new Outcome(brull, null, null, TitleMatch.None, brullFacts, brullVerdict, null),
                new Outcome(polonia, poloniaArticle, null, TitleMatch.None, poloniaFacts, poloniaVerdict, null),
                new Outcome(merli, null, null, TitleMatch.None, null, merliVerdict, null)
            ],
            [
                SeriesIdRecord.Create("3cat", brull, null, brullFacts, brullVerdict, Today, SeriesMatchInfo.IdentifierMethod),
                SeriesIdRecord.Create("3cat", polonia, poloniaArticle, poloniaFacts, poloniaVerdict, Today)
            ],
            [],
            TimeSpan.FromSeconds(2));
    }

    private static BuildResult Single(ProgramEntry program)
        => new([Unmatched(program)], [], [], TimeSpan.Zero);

    private static Outcome Unmatched(ProgramEntry program)
        => new(program, null, null, TitleMatch.None, null, new MatchVerdict(0, MatchState.Unmatched, string.Empty), null);

    private static ProgramEntry Program(string id, string title, string slug, string category, string? original, string url)
        => new()
        {
            Id = id,
            Title = title,
            OriginalTitle = original,
            Category = category,
            Slug = slug,
            Url = url,
            IsTelevision = true
        };

    private static string Url(string slug) => $"https://www.3cat.cat/3cat/{slug}/";
}
