using System.Net;
using System.Text;
using System.Text.Json;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Reads the search endpoint of the Wikipedias and the SPARQL endpoint of Wikidata.
/// </summary>
/// <remarks>
/// <para>
/// The entry point is a search of a Wikipedia, not the item search of Wikidata: <c>wbsearchentities</c>
/// answers with the items whose label contains the words («Polònia» returns the country and its national
/// teams), while the search of the Catalan Wikipedia reaches the article of the programme
/// («Polònia (programa)») and its <c>pageprops</c> publish the item. One single request does both things,
/// because <c>generator=search</c> accepts <c>prop=pageprops</c>.
/// </para>
/// <para>
/// The identifiers and the type of the items are then read with one SPARQL query per fifty items, which
/// asks only for the properties the dataset publishes: <c>wbgetentities</c> would return every claim of
/// every item, several megabytes for a batch of programmes of the catalog.
/// </para>
/// <para>
/// The endpoints are public and shared with every other user of Wikidata, so the client sends an
/// identifiable user agent, keeps a pause between requests and repeats the transient failures (429 and
/// 503) instead of hammering the servers.
/// </para>
/// </remarks>
internal sealed class WikidataClient : IDisposable
{
    /// <summary>Items of a single SPARQL query.</summary>
    internal const int ItemsPerQuery = 50;

    /// <summary>Articles read from the search of a Wikipedia.</summary>
    internal const int SearchLimit = 5;

    /// <summary>Item of the television series of Wikidata, the type the matcher looks for.</summary>
    internal const string TelevisionSeries = "Q5398426";

    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly int _delay;
    private readonly bool _allowNetwork;
    private readonly Action<string>? _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="WikidataClient"/> class.
    /// </summary>
    /// <param name="userAgent">User agent, which identifies the tool and a way to contact its author.</param>
    /// <param name="delay">Pause between two requests, in milliseconds.</param>
    /// <param name="allowNetwork">Whether the client may reach the network.</param>
    /// <param name="log">Sink of the warnings, or <c>null</c>.</param>
    internal WikidataClient(string userAgent, int delay, bool allowNetwork, Action<string>? log)
    {
        _delay = Math.Max(0, delay);
        _allowNetwork = allowNetwork;
        _log = log;
        _http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }


    /// <summary>
    /// Searches an article of a Wikipedia by its title, with the Wikidata item it publishes.
    /// </summary>
    /// <param name="language">Language of the Wikipedia («ca», «es», «en»).</param>
    /// <param name="title">Title to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The articles found, best first, with the item of each one; an empty list when the search found
    /// none, and <c>null</c> when the request failed (which is what tells the cache not to keep it).
    /// </returns>
    internal async Task<IReadOnlyList<ArticleHit>?> SearchAsync(
        string language,
        string title,
        CancellationToken cancellationToken)
    {
        var url = new StringBuilder("https://")
            .Append(language)
            .Append(".wikipedia.org/w/api.php?action=query&format=json&formatversion=2")
            .Append("&generator=search&gsrnamespace=0&gsrlimit=")
            .Append(SearchLimit)
            .Append("&prop=pageprops&ppprop=wikibase_item&gsrsearch=")
            .Append(Uri.EscapeDataString($"\"{title}\""))
            .ToString();

        var answer = await SendAsync<MediaWikiAnswer>(url, cancellationToken).ConfigureAwait(false);
        if (answer is null)
        {
            return null;
        }

        if (answer.Error is { } error)
        {
            _log?.Invoke($"Wikipedia {language}: {error.Code} {error.Info} buscando «{title}».");
            return null;
        }

        var pages = answer.Query?.Pages;
        if (pages is null)
        {
            return [];
        }

        return pages
            .Where(page => !string.IsNullOrEmpty(page.Title) && !string.IsNullOrEmpty(page.PageProps?.WikidataItem))
            .OrderBy(page => page.Index)
            .Select((page, position) => new ArticleHit(
                language,
                page.Title!,
                page.PageProps!.WikidataItem!,
                page.Index > 0 ? page.Index - 1 : position))
            .ToList();
    }

    /// <summary>
    /// Reads the identifiers and the type of a batch of items.
    /// </summary>
    /// <param name="qids">Items to read; at most <see cref="ItemsPerQuery"/> of them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The facts of each item, keyed by item id; an item without facts is absent.</returns>
    internal async Task<IReadOnlyDictionary<string, EntityFacts>> GetFactsAsync(
        IReadOnlyCollection<string> qids,
        CancellationToken cancellationToken)
    {
        if (qids.Count == 0 || qids.Count > ItemsPerQuery)
        {
            throw new ArgumentOutOfRangeException(nameof(qids), qids.Count, $"Un lote de 1 a {ItemsPerQuery} elementos.");
        }

        var url = "https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(Query(qids));
        var answer = await SendAsync<SparqlAnswer>(url, cancellationToken).ConfigureAwait(false);
        var facts = new Dictionary<string, EntityFacts>(StringComparer.Ordinal);

        foreach (var row in answer?.Results?.Bindings ?? [])
        {
            var qid = Item(row);
            if (qid is null)
            {
                continue;
            }

            if (!facts.TryGetValue(qid, out var item))
            {
                item = new EntityFacts { Qid = qid };
                facts[qid] = item;
            }

            item.Imdb ??= Text(row, "imdb");
            item.Tmdb ??= Text(row, "tmdb");
            item.TmdbMovie ??= Text(row, "tmdbMovie");
            item.Tvdb ??= Text(row, "tvdb");
            item.IsSeries |= string.Equals(Text(row, "isSeries"), "true", StringComparison.OrdinalIgnoreCase);

            if (Year(row) is { } year)
            {
                // The start time of an item is published once, but the row repeats it with every
                // identifier: the earliest one is the year the work started.
                item.StartYear = item.StartYear is { } known ? Math.Min(known, year) : year;
            }
        }

        return facts;
    }

    /// <summary>
    /// Builds the SPARQL query of a batch of items.
    /// </summary>
    /// <param name="qids">Items to read.</param>
    /// <returns>The query.</returns>
    internal static string Query(IEnumerable<string> qids)
        => new StringBuilder()
            .AppendLine("SELECT ?item ?imdb ?tmdb ?tmdbMovie ?tvdb ?start ?isSeries WHERE {")
            .Append("  VALUES ?item { ")
            .AppendJoin(' ', qids.Select(qid => $"wd:{qid}"))
            .AppendLine(" }")
            .AppendLine("  OPTIONAL { ?item wdt:P345 ?imdb }")
            .AppendLine("  OPTIONAL { ?item wdt:P4983 ?tmdb }")
            .AppendLine("  OPTIONAL { ?item wdt:P4947 ?tmdbMovie }")
            .AppendLine("  OPTIONAL { ?item wdt:P4835 ?tvdb }")
            .AppendLine("  OPTIONAL { ?item wdt:P580 ?start }")
            .Append("  BIND(EXISTS { ?item wdt:P31/wdt:P279* wd:")
            .Append(TelevisionSeries)
            .AppendLine(" } AS ?isSeries)")
            .AppendLine("}")
            .ToString();

    /// <summary>
    /// Reads every item that states an identifier of an emitter.
    /// </summary>
    /// <param name="property">Property of Wikidata the identifier belongs to (<c>P10356</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The item of each value, keyed by the value of the property; <c>null</c> when the request failed, which
    /// is what tells the caller not to lose the answer it already had.
    /// </returns>
    /// <remarks>
    /// One request for the whole of Wikidata instead of one per programme: the map is small (a few hundred
    /// statements) and it is what lets the builder skip the search of the programmes Wikidata already links.
    /// </remarks>
    internal async Task<Dictionary<string, string>?> GetIdentifiersAsync(
        string property,
        CancellationToken cancellationToken)
    {
        var url = "https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(IdentifiersQuery(property));
        var answer = await SendAsync<SparqlAnswer>(url, cancellationToken).ConfigureAwait(false);
        if (answer is null)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in answer.Results?.Bindings ?? [])
        {
            var value = Text(row, "id");
            var qid = Item(row);

            if (value is null || qid is null)
            {
                continue;
            }

            if (!map.TryAdd(value, qid))
            {
                _log?.Invoke($"«{value}» es el valor de {property} en dos elementos: se usa {map[value]}.");
            }
        }

        return map;
    }

    /// <summary>
    /// Builds the SPARQL query of the identifiers of an emitter.
    /// </summary>
    /// <param name="property">Property of Wikidata the identifier belongs to (<c>P10356</c>).</param>
    /// <returns>The query.</returns>
    /// <exception cref="ArgumentException">The property is not one of Wikidata.</exception>
    /// <remarks>
    /// The property arrives from the descriptor of an emitter, not from the command line, but the query is
    /// built by joining strings: a value that is not a property would be a query and not an identifier.
    /// </remarks>
    internal static string IdentifiersQuery(string property)
    {
        if (property.Length < 2
            || property[0] != 'P'
            || !int.TryParse(
                property.AsSpan(1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
        {
            throw new ArgumentException($"«{property}» no es una propiedad de Wikidata.", nameof(property));
        }

        return new StringBuilder("SELECT ?item ?id WHERE { ?item wdt:")
            .Append(property)
            .AppendLine(" ?id }")
            .ToString();
    }

    private static string? Item(Dictionary<string, SparqlValue> row)
    {
        var value = Text(row, "item");
        if (value is null)
        {
            return null;
        }

        var slash = value.LastIndexOf('/');

        return ExternalIds.Wikidata(slash >= 0 ? value[(slash + 1)..] : value);
    }

    private static string? Text(Dictionary<string, SparqlValue> row, string name)
        => row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value.Value)
            ? value.Value.Trim()
            : null;

    private static int? Year(Dictionary<string, SparqlValue> row)
    {
        var value = Text(row, "start");

        return value is { Length: >= 4 }
            && int.TryParse(
                value.AsSpan(0, 4),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var year)
            && year > 1900
                ? year
                : null;
    }

    private async Task<T?> SendAsync<T>(string url, CancellationToken cancellationToken)
        where T : class
    {
        if (!_allowNetwork)
        {
            return null;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (_delay > 0)
                {
                    await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
                }

                using var response = await _http
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content
                        .ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false);

                    return await JsonSerializer
                        .DeserializeAsync<T>(stream, DatasetJson.Options, cancellationToken)
                        .ConfigureAwait(false);
                }

                if (!IsTransient(response.StatusCode) || attempt >= MaxAttempts)
                {
                    _log?.Invoke($"Wikidata respondió {(int)response.StatusCode} a {Short(url)}.");
                    return null;
                }
            }
            catch (Exception ex) when (
                (ex is HttpRequestException || ex is TaskCanceledException)
                && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= MaxAttempts)
                {
                    _log?.Invoke($"Wikidata no respondió a {Short(url)}: {ex.Message}");
                    return null;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsTransient(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static string Short(string url) => url.Length <= 120 ? url : url[..120] + "…";

    /// <summary>
    /// Releases the HTTP client.
    /// </summary>
    public void Dispose() => _http.Dispose();
}
