namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Where a person checks a record: the page of the programme in the emitter, the item of Wikidata and the
/// article the matcher read.
/// </summary>
/// <remarks>
/// The links are built once here and used twice: the review file carries them in its own columns, so a
/// spreadsheet opens them with a click, and the interactive review prints them in the terminal. Every value
/// goes through <see cref="ExternalIds"/> first, so a link is never built from a value the dataset would
/// have dropped.
/// </remarks>
internal static class Urls
{
    /// <summary>
    /// Reads the page of the programme published by the emitter.
    /// </summary>
    /// <param name="url">URL of the dump, or <c>null</c> when it publishes none.</param>
    /// <returns>The URL, or <c>null</c>.</returns>
    internal static string? Program(string? url)
        => string.IsNullOrWhiteSpace(url) ? null : url.Trim();

    /// <summary>
    /// Builds the page of a Wikidata item.
    /// </summary>
    /// <param name="qid">Item (<c>Q2688944</c>).</param>
    /// <returns>The URL, or <c>null</c> when the value is not an item id.</returns>
    internal static string? Wikidata(string? qid)
        => ExternalIds.Wikidata(qid) is { } item ? $"https://www.wikidata.org/wiki/{item}" : null;

    /// <summary>
    /// Builds the page of a title in IMDb.
    /// </summary>
    /// <param name="id">IMDb id (<c>tt4644488</c>).</param>
    /// <returns>The URL, or <c>null</c> when the value is not an IMDb id.</returns>
    internal static string? Imdb(string? id)
        => ExternalIds.Imdb(id) is { } title ? $"https://www.imdb.com/title/{title}/" : null;

    /// <summary>
    /// Builds the article of a Wikipedia.
    /// </summary>
    /// <param name="language">Language of the Wikipedia (<c>ca</c>, <c>es</c>, <c>en</c>).</param>
    /// <param name="title">Title of the article, with the spaces the search answered.</param>
    /// <returns>The URL, or <c>null</c> when the language or the title are missing.</returns>
    internal static string? Wikipedia(string? language, string? title)
        => string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(title)
            ? null
            : $"https://{language.Trim()}.wikipedia.org/wiki/"
                + Uri.EscapeDataString(title.Trim().Replace(' ', '_'));
}
