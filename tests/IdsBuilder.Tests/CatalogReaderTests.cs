using System.Text;
using OpenTvIds.IdsBuilder;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of <see cref="CatalogReader"/>: the dump of <c>tools/CatalogDump</c> quotes every field and separates
/// it with «;», so the reader must honour the quotes before it splits anything.
/// </summary>
public class CatalogReaderTests
{
    private const string Header =
        "\"id\";\"titol\";\"kind\";\"category\";\"detail_title\";\"detail_original_title\";"
        + "\"detail_countries\";\"primera_emissio\";\"error\"";

    [Fact]
    public void ParseKeepsTheSeparatorThatIsInsideAQuotedField()
    {
        var rows = CatalogReader.Parse(Header + "\r\n\"1\";\"Crims; el jurat\";\"TV\"\r\n");

        Assert.Equal(9, rows[0].Length);
        Assert.Equal("Crims; el jurat", rows[1][1]);
    }

    [Fact]
    public void ParseReadsAnEscapedQuote()
    {
        var rows = CatalogReader.Parse(Header + "\r\n\"1\";\"El \"\"millor\"\" dia\"\r\n");

        Assert.Equal("El \"millor\" dia", rows[1][1]);
    }

    [Fact]
    public void ParseIgnoresTheByteOrderMark()
    {
        var rows = CatalogReader.Parse("\uFEFF\"id\";\"titol\"\r\n\"1\";\"Merlí\"\r\n");

        Assert.Equal("id", rows[0][0]);
        Assert.Equal("Merlí", rows[1][1]);
    }

    [Fact]
    public void ParseReadsTheLastRowWithoutATrailingNewline()
    {
        var rows = CatalogReader.Parse("\"id\";\"titol\"\r\n\"1\";\"Merlí\"");

        Assert.Equal(2, rows.Count);
        Assert.Equal("Merlí", rows[1][1]);
    }

    [Fact]
    public void ReadSkipsTheWebAndRadioProgrammes()
    {
        var programs = Read(
            Row("1", "Polònia", "TV", "Humor", "2006-02-16 00:00:00"),
            Row("2", "Crims", "Web/radio", "Documental", null));

        var program = Assert.Single(programs);
        Assert.Equal("Polònia", program.Title);
        Assert.True(program.IsTelevision);
        Assert.Equal(2006, program.Year);
        Assert.Equal("humour", program.Kind);
    }

    [Fact]
    public void ReadIncludesTheWebProgrammesWhenAsked()
    {
        var programs = Read(
            includeWeb: true,
            Row("1", "Polònia", "TV", "Humor", "2006-02-16 00:00:00"),
            Row("2", "Crims", "Web/radio", "Documental", null));

        Assert.Equal(2, programs.Count);
        Assert.False(programs[1].IsTelevision);
        Assert.Null(programs[1].Year);
    }

    [Fact]
    public void ReadSkipsARowWithoutIdOrTitle()
    {
        var programs = Read(
            Row(null, "Polònia", "TV", "Humor", null),
            Row("2", null, "TV", "Humor", null),
            Row("3", "Merlí", "TV", "Sèrie", "2015-09-14 21:55:08"));

        var program = Assert.Single(programs);
        Assert.Equal("3", program.Id);
    }

    [Fact]
    public void ReadRejectsAFileThatIsNotADump()
    {
        var path = Path.Combine(Path.GetTempPath(), $"3cat-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "\"foo\";\"bar\"\r\n\"1\";\"2\"\r\n", Encoding.UTF8);

        try
        {
            var exception = Assert.Throws<InvalidDataException>(() => CatalogReader.Read(path));
            Assert.Contains("id", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Sèrie", "series")]
    [InlineData("Animació", "animation")]
    [InlineData("Anime", "anime")]
    [InlineData("Cinema", "film")]
    [InlineData("Documental", "documentary")]
    [InlineData("Actualitat", "news")]
    [InlineData("Magazín", "magazine")]
    [InlineData("Concurs", "contest")]
    [InlineData("Humor", "humour")]
    [InlineData("Música", "music")]
    [InlineData("Teatre", "theatre")]
    [InlineData("Esports", "sports")]
    [InlineData("Entreteniment", "entertainment")]
    [InlineData("Especial", "special")]
    [InlineData("", "unknown")]
    [InlineData(null, "unknown")]
    public void KindOfTranslatesTheCategoryOfThePlugin(string? category, string expected)
        => Assert.Equal(expected, ProgramEntry.KindOf(category));

    [Fact]
    public void LookupsReadTheOriginalTitleWhenTheCatalogPublishesOne()
    {
        var program = new ProgramEntry
        {
            Id = "73476",
            Title = "Bola de Drac Súper",
            DetailTitle = "Bola de Drac Súper",
            OriginalTitle = "Dragon Ball Super",
            Category = "Anime",
            IsTelevision = true
        };

        var lookups = program.Lookups();

        Assert.Equal(4, lookups.Count);
        Assert.Equal(new LookupTitle("Bola de Drac Súper", "ca"), lookups[0]);
        Assert.Equal(new LookupTitle("Dragon Ball Super", "en"), lookups[1]);
        Assert.Equal(4, lookups.Distinct().Count());
    }

    [Fact]
    public void LookupsDoNotRepeatTheSameTitle()
    {
        var program = new ProgramEntry
        {
            Id = "1",
            Title = "Merlí",
            DetailTitle = "Merlí",
            Category = "Sèrie",
            IsTelevision = true
        };

        var lookups = program.Lookups();

        Assert.Equal(3, lookups.Count);
        Assert.Equal(new LookupTitle("Merlí", "ca"), lookups[0]);
        Assert.Equal(new LookupTitle("Merlí", "es"), lookups[1]);
        Assert.Equal(new LookupTitle("Merlí", "en"), lookups[2]);
    }

    private static string Row(string? id, string? title, string kind, string category, string? firstAired)
        => $"\"{id}\";\"{title}\";\"{kind}\";\"{category}\";\"{title}\";\"\";\"\";\"{firstAired}\";\"\"";

    private static List<ProgramEntry> Read(params string[] rows) => Read(false, rows);

    private static List<ProgramEntry> Read(bool includeWeb, params string[] rows)
    {
        var path = Path.Combine(Path.GetTempPath(), $"3cat-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, Header + "\r\n" + string.Join("\r\n", rows) + "\r\n", new UTF8Encoding(true));

        try
        {
            return CatalogReader.Read(path, includeWeb);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
