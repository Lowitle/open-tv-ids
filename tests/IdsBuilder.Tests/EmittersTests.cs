using OpenTvIds.IdsBuilder;
using OpenTvIds.IdsBuilder.Emitters;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of the emitter layer: the catalog of an emitter is its CSV dump, and every file of a build takes the
/// slug of the emitter, so a second emitter needs no code.
/// </summary>
public class EmittersTests
{
    [Fact]
    public void TheCatalogOfAnEmitterReadsItsDumpAndKeepsTheSlug()
    {
        var path = Path.Combine(Path.GetTempPath(), $"emisor-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, """
            "id";"titol";"kind";"category";"detail_original_title"
            "73476";"Bola de Drac Súper";"TV";"Anime";"Dragon Ball Super"
            "1";"Gent amb DO";"WEB";"Documental";
            """);

        try
        {
            IProgramCatalog catalog = new CsvCatalog(path, "3cat");

            var program = Assert.Single(catalog.Read(includeWeb: false));

            Assert.Equal("3cat", catalog.Emitter);
            Assert.Equal("73476", program.Id);
            Assert.Equal("Bola de Drac Súper", program.Title);
            Assert.Equal("Dragon Ball Super", program.OriginalTitle);
            Assert.Equal("anime", program.Kind);
            Assert.True(program.IsTelevision);

            // The Web and radio programmes are there, but the reader leaves them out unless it is asked.
            Assert.Equal(2, catalog.Read(includeWeb: true).Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheFilesOfTheBuildTakeTheSlugOfTheEmitter()
    {
        var options = new Options { Emitter = "rtve" };

        Assert.Equal(Path.Combine(".", "data", "rtve", "series.jsonl"), options.DatasetPath);
        Assert.Equal(Path.Combine(".", "report", "rtve", "revision.csv"), options.ReviewPath);
        Assert.Equal(Path.Combine(".", "report", "rtve", "cobertura.md"), options.ReportPath);
        Assert.Equal(Path.Combine(".", "overrides", "rtve.json"), options.OverridesPath);
        Assert.Equal(Path.Combine("input", "rtve.csv"), options.InputPath);

        // An explicit dump wins over the default of the emitter.
        var explicitInput = new Options { Emitter = "rtve", InputPath = "catalogo.csv" };

        Assert.Equal("catalogo.csv", explicitInput.InputPath);
    }
}
