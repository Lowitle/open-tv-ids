namespace OpenTvIds.IdsBuilder;

/// <summary>
/// The programmes whose page Wikidata already links to an item: the identifier the emitter publishes in the
/// address of the page, read the other way round.
/// </summary>
/// <remarks>
/// <para>
/// 3Cat publishes the page of a programme at <c>https://www.3cat.cat/3cat/els-barrufets-3d/</c>, and Wikidata
/// states that page in the item as <c>P10356</c> («TV3 show ID», «3Cat show ID»), with the slug of the
/// address as value. When the statement exists there is nothing to guess: the item of the programme is
/// known without searching a single title, which is the only way the matcher reaches the programmes that
/// have no article.
/// </para>
/// <para>
/// The map is read in one request for the whole of Wikidata and cached as a whole. A build with the network
/// on always asks again, because the statement is what grows over time (it is also what a volunteer adds in
/// the catalogs of the community); a build with <c>--offline</c> uses the last copy.
/// </para>
/// </remarks>
internal static class ShowIds
{
    /// <summary>Area of the cache where the map lives.</summary>
    internal const string CacheArea = "showids";

    /// <summary>Key of the map in the cache, which is one answer for every item of Wikidata.</summary>
    internal const string CacheKey = "wikidata";

    /// <summary>
    /// Gets the link of an emitter to Wikidata: the property that states the identifier of a programme and
    /// how the value of a programme is read.
    /// </summary>
    /// <param name="emitter">Emitter of the programmes (<c>3cat</c>).</param>
    /// <returns>
    /// The link, or <c>null</c> when Wikidata has no property for the catalog of the emitter, which is the
    /// case of every catalog whose pages are not filed in Wikidata yet.
    /// </returns>
    /// <remarks>
    /// <c>P10356</c> is a property of Wikidata, so the value is not the id of the dump (<c>73476</c>) but the
    /// slug of the page (<c>els-barrufets-3d</c>), which is what the dump publishes in <c>slug</c> and at the
    /// end of <c>url</c>. An emitter adds its property here the day Wikidata has one; until then its
    /// programmes are matched by title, as before.
    /// </remarks>
    internal static ShowIdLink? LinkOf(string emitter) => emitter switch
    {
        "3cat" => new ShowIdLink("P10356", program => program.Slug ?? Slug(program.Url)),
        _ => null
    };

    /// <summary>
    /// Reads the slug of a page from the address of the page (<c>els-barrufets-3d</c>).
    /// </summary>
    /// <param name="url">Address of the page of the programme.</param>
    /// <returns>The slug, or <c>null</c> when the address has no page after the host.</returns>
    internal static string? Slug(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var address = url.Trim();

        var end = address.IndexOfAny(['?', '#']);
        if (end >= 0)
        {
            address = address[..end];
        }

        address = address.TrimEnd('/');

        var host = address.IndexOf("://", StringComparison.Ordinal);
        var slash = address.LastIndexOf('/');

        // «https://www.3cat.cat» has a host and no page: the last segment is the host, not a slug.
        if (slash < 0 || slash <= (host >= 0 ? host + 2 : -1))
        {
            return null;
        }

        var slug = address[(slash + 1)..].Trim();

        return slug.Length == 0 ? null : Uri.UnescapeDataString(slug);
    }

    /// <summary>
    /// Reads the map of the identifiers of an emitter to the items of Wikidata.
    /// </summary>
    /// <param name="property">Property that states the identifier (<c>P10356</c>).</param>
    /// <param name="wikidata">Client of Wikidata.</param>
    /// <param name="cache">Cache of the answers of the endpoints.</param>
    /// <param name="log">Sink of the warnings, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The item of each value of the property, keyed by the value; empty when none is known.</returns>
    internal static async Task<Dictionary<string, string>> ReadAsync(
        string property,
        WikidataClient wikidata,
        Cache cache,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        var cached = cache.TryGet<ShowIdAnswer>(CacheArea, CacheKey, out var stored)
            && stored is not null
            && string.Equals(stored.Property, property, StringComparison.Ordinal)
                ? stored
                : null;

        var answer = await wikidata.GetIdentifiersAsync(property, cancellationToken).ConfigureAwait(false);

        if (answer is null)
        {
            // Sin red (--offline) o con Wikidata en un mal momento: la última copia es mejor que nada, y es
            // lo único que hay en un volcado offline.
            log?.Invoke(cached is null
                ? $"Sin respuesta de Wikidata a «{property}»: ningún programa se casa por identificador."
                : $"Sin respuesta de Wikidata a «{property}»: se usa la copia de la caché, {cached.Ids.Count} identificadores.");

            return cached is null ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) : Map(cached.Ids);
        }

        cache.Set(CacheArea, CacheKey, new ShowIdAnswer { Property = property, Ids = answer });

        return answer;
    }

    /// <summary>
    /// Copies a map with the comparer of the slugs, which a round trip through JSON does not keep.
    /// </summary>
    /// <param name="ids">The identifiers read from the cache.</param>
    /// <returns>The map.</returns>
    private static Dictionary<string, string> Map(IEnumerable<KeyValuePair<string, string>> ids)
        => new(ids, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The link of a catalog to Wikidata: the property that states the identifier of its programmes.
/// </summary>
/// <param name="Property">Property of Wikidata (<c>P10356</c>).</param>
/// <param name="ValueOf">Value the property states for a programme, or <c>null</c> when it has none.</param>
internal sealed record ShowIdLink(string Property, Func<ProgramEntry, string?> ValueOf);

/// <summary>
/// A programme whose page Wikidata already links to an item.
/// </summary>
/// <param name="Property">Property that states the identifier (<c>P10356</c>).</param>
/// <param name="Value">Value of the property for the programme (<c>els-barrufets-3d</c>).</param>
/// <param name="Qid">Item the property points to.</param>
internal sealed record ShowIdMatch(string Property, string Value, string Qid);

/// <summary>
/// The answer of Wikidata kept in the cache: every item that states an identifier of an emitter.
/// </summary>
internal sealed class ShowIdAnswer
{
    /// <summary>Gets or sets the property the identifiers belong to (<c>P10356</c>).</summary>
    public string Property { get; set; } = string.Empty;

    /// <summary>Gets or sets the item of each value, keyed by the value of the property.</summary>
    public Dictionary<string, string> Ids { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
