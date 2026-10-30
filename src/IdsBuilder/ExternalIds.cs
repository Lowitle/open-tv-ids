using System.Text.RegularExpressions;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Checks the shape of the identifiers before they reach the dataset.
/// </summary>
/// <remarks>
/// A value that does not match the shape its catalog publishes is dropped instead of published: an IMDb
/// id that does not start with <c>tt</c> is a Wikibase item id written in the wrong property, and a TMDB
/// id with letters is a URL fragment. Nothing downstream (the plugin, a consumer of the dataset) can
/// recover from that, so the item is left without the identifier and the review file shows it.
/// </remarks>
internal static partial class ExternalIds
{
    /// <summary>
    /// Reads an IMDb id.
    /// </summary>
    /// <param name="value">Value published by Wikidata.</param>
    /// <returns>The id, or <c>null</c> when it is not an IMDb id.</returns>
    internal static string? Imdb(string? value)
        => Clean(value, ImdbRegex());

    /// <summary>
    /// Reads a TMDB id (P4983 or P4947).
    /// </summary>
    /// <param name="value">Value published by Wikidata.</param>
    /// <returns>The id, or <c>null</c> when it is not a TMDB id.</returns>
    internal static string? Tmdb(string? value)
        => Clean(value, NumericRegex());

    /// <summary>
    /// Reads a TVDB id.
    /// </summary>
    /// <param name="value">Value published by Wikidata.</param>
    /// <returns>The id, or <c>null</c> when it is not a TVDB id.</returns>
    internal static string? Tvdb(string? value)
        => Clean(value, NumericRegex());

    /// <summary>
    /// Reads a Wikidata item id.
    /// </summary>
    /// <param name="value">Value published by Wikidata.</param>
    /// <returns>The id, or <c>null</c> when it is not an item id.</returns>
    internal static string? Wikidata(string? value)
        => Clean(value, WikidataRegex());

    [GeneratedRegex(@"^tt\d{7,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImdbRegex();

    [GeneratedRegex(@"^\d{1,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericRegex();

    [GeneratedRegex(@"^Q\d{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex WikidataRegex();

    private static string? Clean(string? value, Regex regex)
    {
        var trimmed = value?.Trim();

        return trimmed is not null && regex.IsMatch(trimmed) ? trimmed : null;
    }
}
