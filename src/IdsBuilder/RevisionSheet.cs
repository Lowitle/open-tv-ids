using System.Globalization;
using System.Text;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// A row of <c>report/&lt;emitter&gt;/revision.csv</c>: a programme of the catalog with what the matcher
/// found and what it decided.
/// </summary>
/// <remarks>
/// The review file is written for a spreadsheet, so every value is text: the confidence arrives with a comma
/// and the identifiers may be empty. The row keeps them as they come and the review parses what it needs, so
/// the file stays the artefact a person reads and the tool reads the same thing.
/// </remarks>
internal sealed class RevisionRow
{
    /// <summary>Gets the global id of the record (<c>3cat:73476</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Gets the id of the programme in the catalog of the emitter (<c>73476</c>).</summary>
    public string EmitterId { get; init; } = string.Empty;

    /// <summary>Gets the category of the emitter («Sèrie», «Animació»...).</summary>
    public string? Category { get; init; }

    /// <summary>Gets the kind of work in ASCII (<c>series</c>, <c>anime</c>...).</summary>
    public string? Kind { get; init; }

    /// <summary>Gets the state the build gave the programme (<c>publicado</c>, <c>revisar</c>...).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Gets the confidence of the match, from 0 to 1.</summary>
    public double Confidence { get; init; }

    /// <summary>Gets the evidence that added the points («título exacto»...).</summary>
    public string? Evidence { get; init; }

    /// <summary>Gets the title of the programme in the catalog.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the original title, when the catalog publishes one.</summary>
    public string? OriginalTitle { get; init; }

    /// <summary>Gets the language of the article the matcher read.</summary>
    public string? ArticleLanguage { get; init; }

    /// <summary>Gets the title of the article the matcher read.</summary>
    public string? ArticleTitle { get; init; }

    /// <summary>Gets the item of the article the matcher read.</summary>
    public string? ArticleQid { get; init; }

    /// <summary>Gets how the title of the programme matches the one of the article.</summary>
    public string? Match { get; init; }

    /// <summary>Gets the item the programme would publish.</summary>
    public string? Wikidata { get; init; }

    /// <summary>Gets the IMDb id the programme would publish.</summary>
    public string? Imdb { get; init; }

    /// <summary>Gets the TMDB id the programme would publish.</summary>
    public string? Tmdb { get; init; }

    /// <summary>Gets the TVDB id the programme would publish.</summary>
    public string? Tvdb { get; init; }

    /// <summary>Gets the title of the best article of a search that did not match.</summary>
    public string? Suggestion { get; init; }

    /// <summary>Gets the item of that article.</summary>
    public string? SuggestionQid { get; init; }

    /// <summary>Gets the page of the programme in the catalog of the emitter.</summary>
    public string? ProgramUrl { get; init; }

    /// <summary>Gets the note of the correction a person already wrote, when there is one.</summary>
    public string? Note { get; init; }

    /// <summary>Gets a value indicating whether the programme is waiting for a person.</summary>
    public bool NeedsReview => State == "revisar";

    /// <summary>Gets a value indicating whether the matcher found nothing for the programme.</summary>
    public bool Unmatched => State == "sin_match";

    /// <summary>
    /// Reads the identifiers the row would publish.
    /// </summary>
    /// <returns>The identifiers that are not empty, keyed by catalog.</returns>
    internal Dictionary<string, string> Identifiers()
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        Add(ids, "wikidata", Wikidata);
        Add(ids, "imdb", Imdb);
        Add(ids, "tmdb", Tmdb);
        Add(ids, "tvdb", Tvdb);

        return ids;
    }

    private static void Add(Dictionary<string, string> ids, string catalog, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            ids[catalog] = value.Trim();
        }
    }
}

/// <summary>
/// Reads <c>report/&lt;emitter&gt;/revision.csv</c>, the file the build writes for a person to review.
/// </summary>
internal static class RevisionSheet
{
    /// <summary>
    /// Reads the review file.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="emitter">Emitter of the programmes, which prefixes the global id.</param>
    /// <returns>The rows, in the order of the file.</returns>
    /// <exception cref="InvalidDataException">The file is not the review file of a build.</exception>
    internal static List<RevisionRow> Load(string path, string emitter)
    {
        var rows = CatalogReader.Parse(File.ReadAllText(path, Encoding.UTF8));
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"«{path}» está vacío.");
        }

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < rows[0].Length; index++)
        {
            columns[rows[0][index].Trim()] = index;
        }

        if (!columns.ContainsKey("id") || !columns.ContainsKey("estado"))
        {
            throw new InvalidDataException(
                $"«{path}» no parece el informe de un volcado: faltan las columnas «id» o «estado».");
        }

        var sheet = new List<RevisionRow>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            var id = Field(row, columns, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            sheet.Add(new RevisionRow
            {
                Id = $"{emitter}:{id}",
                EmitterId = id,
                Category = Field(row, columns, "categoria"),
                Kind = Field(row, columns, "tipo"),
                State = Field(row, columns, "estado") ?? string.Empty,
                Confidence = Score(Field(row, columns, "confianza")),
                Evidence = Field(row, columns, "evidencia"),
                Title = Field(row, columns, "titol"),
                OriginalTitle = Field(row, columns, "titol_original"),
                ArticleLanguage = Field(row, columns, "article_wiki"),
                ArticleTitle = Field(row, columns, "article_titol"),
                ArticleQid = Field(row, columns, "article_qid"),
                Match = Field(row, columns, "coincidencia"),
                Wikidata = Field(row, columns, "wikidata"),
                Imdb = Field(row, columns, "imdb"),
                Tmdb = Field(row, columns, "tmdb"),
                Tvdb = Field(row, columns, "tvdb"),
                Suggestion = Field(row, columns, "suggerencia"),
                SuggestionQid = Field(row, columns, "suggerencia_qid"),
                ProgramUrl = Field(row, columns, $"url_{emitter}"),
                Note = Field(row, columns, "nota")
            });
        }

        return sheet;
    }

    /// <summary>
    /// Reads the confidence of the file, which is written with a comma.
    /// </summary>
    /// <param name="value">The value of the column.</param>
    /// <returns>The confidence, or 0 when the column is empty.</returns>
    internal static double Score(string? value)
        => double.TryParse(
                value?.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var score)
            ? score
            : 0;

    private static string? Field(string[] row, Dictionary<string, int> columns, string name)
    {
        if (!columns.TryGetValue(name, out var index) || index >= row.Length)
        {
            return null;
        }

        var value = row[index].Trim();

        return value.Length == 0 ? null : value;
    }
}
