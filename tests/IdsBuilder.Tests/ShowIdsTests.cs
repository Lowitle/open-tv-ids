using OpenTvIds.IdsBuilder;
using Xunit;

namespace OpenTvIds.IdsBuilder.Tests;

/// <summary>
/// Tests of the programmes whose page Wikidata already links: the slug of the address of the page, the
/// property that states it and the copy of the map the build keeps in the cache.
/// </summary>
/// <remarks>
/// The whole step exists because half of the catalog has no article to search: with the identifier of the
/// page in Wikidata there is nothing to guess, so the details matter (a trailing slash, a query string, a
/// comparer that a file does not keep).
/// </remarks>
public class ShowIdsTests
{
    [Fact]
    public void TheSlugIsTheLastSegmentOfTheAddress()
    {
        Assert.Equal("els-barrufets-3d", ShowIds.Slug("https://www.3cat.cat/3cat/els-barrufets-3d/"));
        Assert.Equal("polonia", ShowIds.Slug("https://www.3cat.cat/3cat/polonia"));
        Assert.Equal("polonia", ShowIds.Slug("  https://www.3cat.cat/3cat/polonia/?utm=1#top  "));
        Assert.Equal("nom", ShowIds.Slug("https://example.test/nom"));
    }

    [Fact]
    public void AnAddressWithoutAPageHasNoSlug()
    {
        Assert.Null(ShowIds.Slug(null));
        Assert.Null(ShowIds.Slug(""));
        Assert.Null(ShowIds.Slug("https://www.3cat.cat"));
        Assert.Null(ShowIds.Slug("https://www.3cat.cat/"));
    }

    [Fact]
    public void ThePropertyOfTheCatalogIsTheOneWikidataStates()
    {
        var link = ShowIds.LinkOf("3cat");

        Assert.NotNull(link);
        Assert.Equal("P10356", link.Property);

        // A catalog whose pages are not filed in Wikidata keeps matching by title.
        Assert.Null(ShowIds.LinkOf("rtve"));
    }

    [Fact]
    public void TheValueOfAProgrammeIsTheColumnOfTheDumpOrTheAddressOfThePage()
    {
        var link = ShowIds.LinkOf("3cat")!;

        Assert.Equal("divergents", link.ValueOf(Program("divergents", "https://www.3cat.cat/3cat/divergents/")));
        Assert.Equal("divergents", link.ValueOf(Program(null, "https://www.3cat.cat/3cat/divergents/")));
        Assert.Null(link.ValueOf(Program(null, null)));
    }

    [Fact]
    public async Task TheMapOfTheCacheIsUsedWhenWikidataDoesNotAnswer()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"showids-{Guid.NewGuid():N}");

        try
        {
            var cache = new Cache(folder, read: true);
            cache.Set(ShowIds.CacheArea, ShowIds.CacheKey, new ShowIdAnswer
            {
                Property = "P10356",
                Ids = new Dictionary<string, string> { ["els-barrufets-3d"] = "Q69652877" }
            });

            using var offline = new WikidataClient("open-tv-ids/tests", delay: 0, allowNetwork: false, log: null);

            var map = await ShowIds.ReadAsync("P10356", offline, cache, log: null, CancellationToken.None);

            Assert.Equal("Q69652877", map["els-barrufets-3d"]);

            // The comparer of the slugs survives the round trip through the file of the cache.
            Assert.Equal("Q69652877", map["ELS-BARRUFETS-3D"]);

            // A copy of another property is not an answer to this one.
            var other = await ShowIds.ReadAsync("P1234", offline, cache, log: null, CancellationToken.None);

            Assert.Empty(other);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheQueryAsksWikidataForEveryItemOfTheProperty()
    {
        Assert.Contains("wdt:P10356", WikidataClient.IdentifiersQuery("P10356"), StringComparison.Ordinal);

        // The property is joined into a query: anything that is not one is refused instead of sent.
        Assert.Throws<ArgumentException>(() => WikidataClient.IdentifiersQuery("3cat"));
    }

    private static ProgramEntry Program(string? slug, string? url)
        => new()
        {
            Id = "1",
            Title = "Divergents",
            Category = "Actualitat",
            Url = url,
            Slug = slug,
            IsTelevision = true
        };
}
