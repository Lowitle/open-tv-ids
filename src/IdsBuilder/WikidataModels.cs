using System.Text.Json.Serialization;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Answer of the search endpoint of a Wikipedia (<c>action=query&amp;generator=search</c>).
/// </summary>
internal sealed class MediaWikiAnswer
{
    /// <summary>Gets or sets the pages the search found.</summary>
    [JsonPropertyName("query")]
    public MediaWikiQuery? Query { get; set; }

    /// <summary>Gets or sets the error the API publishes instead of the pages.</summary>
    [JsonPropertyName("error")]
    public MediaWikiError? Error { get; set; }
}

/// <summary>
/// Body of the answer of the search endpoint.
/// </summary>
internal sealed class MediaWikiQuery
{
    /// <summary>
    /// Gets or sets the pages. With <c>formatversion=2</c> it is an array, and a page that publishes no
    /// <c>pageprops</c> simply has no property.
    /// </summary>
    [JsonPropertyName("pages")]
    public List<MediaWikiPage>? Pages { get; set; }
}

/// <summary>
/// A page of a Wikipedia, with the Wikidata item it is connected to.
/// </summary>
internal sealed class MediaWikiPage
{
    /// <summary>Gets or sets the title of the article.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the position of the article in the answer, 1 being the best match.</summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>Gets or sets the properties of the page.</summary>
    [JsonPropertyName("pageprops")]
    public MediaWikiPageProps? PageProps { get; set; }
}

/// <summary>
/// The properties of a page, of which only the Wikidata item is read.
/// </summary>
internal sealed class MediaWikiPageProps
{
    /// <summary>Gets or sets the Wikidata item of the article (<c>Q2688944</c>).</summary>
    [JsonPropertyName("wikibase_item")]
    public string? WikidataItem { get; set; }
}

/// <summary>
/// An error of the MediaWiki API.
/// </summary>
internal sealed class MediaWikiError
{
    /// <summary>Gets or sets the code of the error.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Gets or sets the text of the error.</summary>
    [JsonPropertyName("info")]
    public string? Info { get; set; }
}

/// <summary>
/// Answer of the SPARQL endpoint of Wikidata (<c>query.wikidata.org/sparql</c>).
/// </summary>
internal sealed class SparqlAnswer
{
    /// <summary>Gets or sets the results.</summary>
    [JsonPropertyName("results")]
    public SparqlResults? Results { get; set; }
}

/// <summary>
/// The rows of a SPARQL answer.
/// </summary>
internal sealed class SparqlResults
{
    /// <summary>Gets or sets the rows, keyed by the name of the variable of the query.</summary>
    [JsonPropertyName("bindings")]
    public List<Dictionary<string, SparqlValue>>? Bindings { get; set; }
}

/// <summary>
/// A value of a row, as the SPARQL endpoint publishes it: a URI for an item and a literal (with its
/// datatype) for an identifier or a date.
/// </summary>
internal sealed class SparqlValue
{
    /// <summary>Gets or sets the kind of the value (<c>uri</c>, <c>literal</c>, <c>typed-literal</c>).</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>Gets or sets the value («http://www.wikidata.org/entity/Q1», «tt0764959», «2006-02-16T00:00:00Z»).</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
