namespace OpenTvIds.IdsBuilder;

/// <summary>
/// A programme of the catalog dump that is a candidate to be cross-linked: the fields of the dump the
/// matcher needs.
/// </summary>
/// <remarks>
/// The dump is written by <c>tools/CatalogDump</c> from the very same client and classifier the
/// synchronization uses, so the id, the title and the category of a row are the ones the plugin writes
/// to the library.
/// </remarks>
internal sealed record ProgramEntry
{
    /// <summary>Gets the 3Cat programme id (<c>programatv_id</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Gets the Catalan title of the programme.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets the title of the page of the programme, which is the title of the search endpoint whenever
    /// the dump could read the page.
    /// </summary>
    public string? DetailTitle { get; init; }

    /// <summary>
    /// Gets the original title written in the card of the programme («Dragon Ball Super»), the strongest
    /// hint for finding the work in another language.
    /// </summary>
    public string? OriginalTitle { get; init; }

    /// <summary>Gets the category the plugin classifies the programme under («Sèrie», «Anime»...).</summary>
    public string? Category { get; init; }

    /// <summary>Gets the countries of origin published by the card of the programme.</summary>
    public string? Countries { get; init; }

    /// <summary>
    /// Gets the date of the oldest video of the programme available on 3Cat, which is the date the
    /// catalog publishes, not the date the work premiered.
    /// </summary>
    public string? AvailableSince { get; init; }

    /// <summary>
    /// Gets the page of the programme in the catalog of the emitter, which is what a person opens to check
    /// that the identifiers found belong to the work.
    /// </summary>
    public string? Url { get; init; }

    /// <summary>Gets a value indicating whether the programme is a television programme.</summary>
    public bool IsTelevision { get; init; }

    /// <summary>
    /// Gets the kind of work, in ASCII, so it can be filtered without dealing with the accents of the
    /// category («Sèrie» is <c>series</c>).
    /// </summary>
    public string Kind => KindOf(Category);

    /// <summary>
    /// Gets the year of <see cref="AvailableSince"/>.
    /// </summary>
    /// <remarks>
    /// It is the year the programme became available on 3Cat, which for a licensed series is the year of
    /// the licence («Bola de Drac Súper» is 2025) and not the year of production (2015): the matcher only
    /// uses it as a positive hint.
    /// </remarks>
    public int? Year
        => AvailableSince is { Length: >= 4 }
            && int.TryParse(AvailableSince.AsSpan(0, 4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var year)
            && year > 1900
                ? year
                : null;

    /// <summary>
    /// Gets the titles to look up, as a pair of title and Wikipedia language, most promising first.
    /// </summary>
    /// <returns>The titles, without repetitions.</returns>
    /// <remarks>
    /// The original title goes before the second language of the Catalan title: a licensed series
    /// («Bola de Drac Súper») is filed in the Catalan Wikipedia under the Catalan name and in the English one
    /// under the original name, so the second request is the one that finds «Dragon Ball Super» instead of a
    /// third attempt of the same title in another language.
    /// </remarks>
    public IReadOnlyList<LookupTitle> Lookups()
    {
        var local = Clean(DetailTitle) ?? Clean(Title);
        var original = Clean(OriginalTitle);
        var lookups = new List<LookupTitle>(4);

        Add(lookups, local, "ca");
        Add(lookups, original, "en");
        Add(lookups, original, "es");
        Add(lookups, local, "es");
        Add(lookups, local, "en");

        return lookups;
    }

    /// <summary>
    /// Translates a category of the plugin into an ASCII kind.
    /// </summary>
    /// <param name="category">Category of the plugin («Sèrie», «Animació»...).</param>
    /// <returns>The kind, or <c>unknown</c> when the catalog publishes no category.</returns>
    internal static string KindOf(string? category)
        => CrossIdTitle.Normalize(CrossIdTitle.BaseTitle(category)) switch
        {
            "serie" => "series",
            "animacio" => "animation",
            "anime" => "anime",
            "cinema" => "film",
            "documental" => "documentary",
            "actualitat" => "news",
            "magazin" => "magazine",
            "concurs" => "contest",
            "humor" => "humour",
            "musica" => "music",
            "teatre" => "theatre",
            "esports" => "sports",
            "entreteniment" => "entertainment",
            "especial" => "special",
            _ => "unknown"
        };

    private static void Add(List<LookupTitle> lookups, string? title, string language)
    {
        if (title is null)
        {
            return;
        }

        // The same title in two languages is worth looking up: 3Cat often publishes the Catalan title
        // and the article only exists in the Spanish Wikipedia («Ermessenda»), and the other way round.
        if (lookups.Any(lookup => lookup.Title == title && lookup.Language == language))
        {
            return;
        }

        if (lookups.Count < 4)
        {
            lookups.Add(new LookupTitle(title, language));
        }
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// A title to look up in a Wikipedia.
/// </summary>
/// <param name="Title">Title of the search.</param>
/// <param name="Language">Language of the Wikipedia («ca», «es», «en»).</param>
internal sealed record LookupTitle(string Title, string Language);
