namespace OpenTvIds.IdsBuilder;

/// <summary>
/// The identifiers and the type of a Wikidata item, read for the properties the dataset publishes.
/// </summary>
/// <remarks>
/// The values are kept as Wikidata publishes them (an IMDb id keeps its <c>tt</c> prefix), and every one
/// of them is checked with <see cref="ExternalIds"/> before it reaches the dataset: the catalogs of
/// Wikidata hold occasional typos and redirects that would otherwise travel to Jellyfin.
/// </remarks>
internal sealed record EntityFacts
{
    /// <summary>Gets or sets the item (<c>Q2688944</c>).</summary>
    public string Qid { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMDb id (P345).</summary>
    public string? Imdb { get; set; }

    /// <summary>Gets or sets the TMDB id of the TV series (P4983).</summary>
    public string? Tmdb { get; set; }

    /// <summary>Gets or sets the TMDB id of the film (P4947).</summary>
    public string? TmdbMovie { get; set; }

    /// <summary>Gets or sets the TVDB id (P4835).</summary>
    public string? Tvdb { get; set; }

    /// <summary>Gets or sets the year of the start time (P580).</summary>
    public int? StartYear { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the item is a television series or a subclass of it
    /// (<c>P31/P279*</c> of Q5398426), which is what tells a work apart from a franchise or a person
    /// that share its title.
    /// </summary>
    public bool IsSeries { get; set; }

    /// <summary>
    /// Gets the TMDB id of the item, whichever of the two properties publishes it.
    /// </summary>
    public string? AnyTmdb => Tmdb ?? TmdbMovie;

    /// <summary>
    /// Gets a value indicating whether the item publishes at least one identifier the dataset wants.
    /// </summary>
    public bool HasAnyId => Imdb is not null || Tmdb is not null || TmdbMovie is not null || Tvdb is not null;
}
