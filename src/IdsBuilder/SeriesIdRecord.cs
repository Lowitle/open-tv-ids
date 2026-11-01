using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// A row of <c>dataset/data/3cat/series.jsonl</c>: the identifiers of a programme of 3Cat in the catalogs
/// of Wikidata, IMDb, TMDB and TVDB.
/// </summary>
/// <remarks>
/// The record is the public contract of the dataset, so it only holds what another tool needs to resolve
/// the same work: the id of the emitter, the ids of the catalogs, where they come from and how confident
/// the match is. It does not hold the synopsis, the images or the episodes: 3Cat publishes those, and the
/// dataset is not a copy of its catalog.
/// </remarks>
internal sealed class SeriesIdRecord
{
    /// <summary>Gets or sets the global id (<c>3cat:73476</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the emitter of <see cref="EmitterId"/> (<c>3cat</c>).</summary>
    public string Emitter { get; set; } = "3cat";

    /// <summary>Gets or sets the id of the work in the catalog of the emitter (<c>programatv_id</c>).</summary>
    public string EmitterId { get; set; } = string.Empty;

    /// <summary>Gets or sets the kind of work, in ASCII (<c>series</c>, <c>anime</c>, <c>documentary</c>...).</summary>
    public string Kind { get; set; } = "unknown";

    /// <summary>Gets or sets the category of the emitter, untranslated («Sèrie», «Animació»...).</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets the title of the work in the catalog of the emitter, in Catalan.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the original title, when the emitter publishes one that differs.</summary>
    public string? OriginalTitle { get; set; }

    /// <summary>
    /// Gets or sets the date the work became available on the emitter, which is the date the catalog
    /// publishes and not necessarily the date it premiered.
    /// </summary>
    public DateOnly? AvailableSince { get; set; }

    /// <summary>Gets or sets the identifiers, keyed by catalog (<c>wikidata</c>, <c>imdb</c>, <c>tmdb</c>...).</summary>
    public Dictionary<string, string> Ids { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets where the record comes from (<c>wikidata:Q2688944</c>, <c>wikipedia:ca:Polònia</c>).</summary>
    public List<string> Sources { get; set; } = [];

    /// <summary>Gets or sets how the record was matched.</summary>
    public SeriesMatchInfo Match { get; set; } = new();

    /// <summary>Gets or sets the first day the record was published.</summary>
    public DateOnly? FirstSeen { get; set; }

    /// <summary>Gets or sets the last day the record was computed.</summary>
    public DateOnly? LastSeen { get; set; }

    /// <summary>
    /// Builds the record of a programme from the article and the item the matcher has chosen.
    /// </summary>
    /// <param name="emitter">The emitter of the programme (<c>3cat</c>), which prefixes the global id.</param>
    /// <param name="program">The programme of the catalog.</param>
    /// <param name="article">The article chosen for it, or <c>null</c> when a person filled it by hand.</param>
    /// <param name="facts">The item of the article, or <c>null</c> when it could not be read.</param>
    /// <param name="verdict">The verdict of the scorer.</param>
    /// <param name="today">Day the dataset is built.</param>
    /// <param name="method">
    /// How the record was matched (<c>wikidata</c> when a search found the article, <c>wikidata-id</c> when
    /// Wikidata already states the identifier of the page of the programme).
    /// </param>
    /// <returns>The record.</returns>
    internal static SeriesIdRecord Create(
        string emitter,
        ProgramEntry program,
        ArticleMatch? article,
        EntityFacts? facts,
        MatchVerdict verdict,
        DateOnly today,
        string method = "wikidata")
    {
        var originalTitle = program.OriginalTitle;
        var record = new SeriesIdRecord
        {
            Id = $"{emitter}:{program.Id}",
            Emitter = emitter,
            EmitterId = program.Id,
            Kind = program.Kind,
            Category = program.Category,
            Title = program.Title,
            OriginalTitle = string.Equals(originalTitle, program.Title, StringComparison.Ordinal) ? null : originalTitle,
            AvailableSince = ReadDate(program.AvailableSince),
            Match = new SeriesMatchInfo { Method = method, Confidence = verdict.Confidence },
            FirstSeen = today,
            LastSeen = today
        };

        record.Add("wikidata", ExternalIds.Wikidata(facts?.Qid ?? article?.Hit.Qid));
        record.Add("imdb", ExternalIds.Imdb(facts?.Imdb));
        record.Add("tmdb", ExternalIds.Tmdb(facts?.AnyTmdb));
        record.Add("tvdb", ExternalIds.Tvdb(facts?.Tvdb));

        if (facts is not null)
        {
            record.Sources.Add($"wikidata:{facts.Qid}");
        }

        if (article is not null)
        {
            record.Sources.Add($"wikipedia:{article.Hit.Language}:{article.Hit.Title}");
        }

        return record;
    }

    /// <summary>
    /// Reads the date the catalog publishes, which is written as <c>2025-06-06 07:00:00</c>.
    /// </summary>
    /// <param name="value">The date of the dump.</param>
    /// <returns>The day, or <c>null</c> when the dump publishes none.</returns>
    /// <remarks>
    /// <see cref="DateOnly.TryParse(string?, out DateOnly)"/> refuses a value with a time, so the whole column
    /// of the dump would have been dropped and every record would have been published without its date.
    /// </remarks>
    private static DateOnly? ReadDate(string? value)
        => value is { Length: >= 10 }
            && DateOnly.TryParseExact(
                value[..10],
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var day)
                ? day
                : null;

    /// <summary>
    /// Adds an identifier, when it is known.
    /// </summary>
    /// <param name="catalog">Name of the catalog.</param>
    /// <param name="value">Identifier, already checked.</param>
    internal void Add(string catalog, string? value)
    {
        if (value is not null)
        {
            Ids[catalog] = value;
        }
    }
}

/// <summary>
/// How a record was matched.
/// </summary>
internal sealed class SeriesMatchInfo
{
    /// <summary>
    /// Method of the records whose item Wikidata already states with the identifier of the page of the
    /// programme, which needs no search by title (<c>wikidata-id</c>).
    /// </summary>
    internal const string IdentifierMethod = "wikidata-id";

    /// <summary>
    /// Method of the records a person confirmed, completed or dismissed by hand in
    /// <c>overrides/&lt;emitter&gt;.json</c> (<c>manual</c>).
    /// </summary>
    internal const string ManualMethod = "manual";

    /// <summary>
    /// Gets or sets the method (<c>wikidata</c> when a search found the article, <c>wikidata-id</c> when
    /// Wikidata states the identifier of the page, <c>manual</c> when a person corrected the record).
    /// </summary>
    public string Method { get; set; } = "wikidata";

    /// <summary>Gets or sets the confidence, from 0 to 1.</summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether a second, independent source confirms the identifiers. The
    /// first phase of the dataset only uses Wikidata, so every record is published as unverified.
    /// </summary>
    public bool Verified { get; set; }
}

/// <summary>
/// The JSON dialect of the dataset.
/// </summary>
internal static class DatasetJson
{
    /// <summary>
    /// Gets the options every file of the dataset is written and read with.
    /// </summary>
    /// <remarks>
    /// The encoder keeps the accents of the Catalan titles readable instead of escaping them: the files are
    /// meant to be reviewed in a diff.
    /// </remarks>
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };
}
