using System.Text;
using System.Text.Json;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Writes the files of the dataset.
/// </summary>
/// <remarks>
/// The dataset is JSON Lines: one record per line, no commas between them, so a diff of the build only
/// touches the records that changed and a consumer can read it line by line without loading the file. The
/// review file is a CSV with «;» as separator and UTF-8 with BOM, the dialect of the dumps the emitters
/// publish, so it opens in Excel with the accents in place.
/// </remarks>
internal static class DatasetWriter
{
    /// <summary>
    /// Columns of the review file, in the order a person reads them.
    /// </summary>
    /// <param name="emitter">Emitter of the programmes, which names the column of the page of the programme.</param>
    /// <returns>The columns.</returns>
    /// <remarks>
    /// The column of the page is named after the emitter (<c>url_3cat</c>) because a report of another emitter
    /// shows the pages of that emitter, and the review uses it as the first link to open.
    /// </remarks>
    private static string[] ReviewColumns(string emitter) =>
    [
        "id",
        "revisado",
        "categoria",
        "tipo",
        "estado",
        "confianza",
        "evidencia",
        "titol",
        "titol_original",
        $"url_{emitter}",
        "url_wikidata",
        "article_wiki",
        "article_titol",
        "article_qid",
        "coincidencia",
        "wikidata",
        "imdb",
        "tmdb",
        "tvdb",
        "suggerencia",
        "suggerencia_qid",
        "url_suggerencia",
        "nota"
    ];

    /// <summary>
    /// Writes the dataset.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="records">The records, already sorted.</param>
    internal static void WriteDataset(string path, IReadOnlyList<SeriesIdRecord> records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        foreach (var record in records)
        {
            writer.Write(JsonSerializer.Serialize(record, DatasetJson.Options));
            writer.Write('\n');
        }
    }

    /// <summary>
    /// Writes the review file, with every programme the build processed and what it decided.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="emitter">Emitter of the programmes, which prefixes the global id.</param>
    /// <param name="result">The result of the build.</param>
    internal static void WriteReview(string path, string emitter, BuildResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
        {
            AutoFlush = false,
            NewLine = "\r\n"
        };

        WriteRow(writer, ReviewColumns(emitter));

        var records = result.Records.ToDictionary(record => record.Id, StringComparer.Ordinal);

        foreach (var outcome in result.Outcomes)
        {
            var record = records.GetValueOrDefault($"{emitter}:{outcome.Program.Id}");
            var qid = Identifier(outcome, record, "wikidata");

            WriteRow(writer, [
                outcome.Program.Id,
                outcome.Manual is null ? "no" : "sí",
                outcome.Program.Category,
                outcome.Program.Kind,
                State(outcome),
                Confidence(outcome.Verdict.Confidence),
                outcome.Verdict.Evidence,
                outcome.Program.Title,
                outcome.Program.OriginalTitle,
                Urls.Program(outcome.Program.Url),
                Urls.Wikidata(qid),
                outcome.Article?.Hit.Language,
                outcome.Article?.Hit.Title,
                outcome.Article?.Hit.Qid,
                outcome.Article?.Match.ToString(),
                qid,
                Identifier(outcome, record, "imdb"),
                Identifier(outcome, record, "tmdb"),
                Identifier(outcome, record, "tvdb"),
                outcome.Suggestion?.Title,
                outcome.Suggestion?.Qid,
                Urls.Wikidata(outcome.Suggestion?.Qid),
                outcome.Manual?.Note
            ]);
        }
    }

    /// <summary>
    /// Names the state of a programme in the review file.
    /// </summary>
    /// <param name="outcome">The programme.</param>
    /// <returns>The state.</returns>
    internal static string State(Outcome outcome)
        => outcome.Manual switch
        {
            { Reject: true } => "descartado",
            not null => "publicado (manual)",
            _ => outcome.Verdict.State switch
            {
                MatchState.Accepted => "publicado",
                MatchState.Review => "revisar",
                _ => "sin_match"
            }
        };

    /// <summary>
    /// Reads the identifier a programme would publish, which is what a person accepts or rejects in the
    /// review: the one of the record when it is published and the one of the item the matcher chose when it
    /// is not.
    /// </summary>
    /// <param name="outcome">The programme.</param>
    /// <param name="record">Its record, or <c>null</c> when it is not published.</param>
    /// <param name="catalog">The catalog (<c>wikidata</c>, <c>imdb</c>...).</param>
    /// <returns>The identifier, or <c>null</c>.</returns>
    /// <remarks>
    /// Without the second half a person could not decide on a programme under review: the review file only
    /// showed the identifiers of the records that were already published, which are exactly the ones that
    /// needed no decision.
    /// </remarks>
    internal static string? Identifier(Outcome outcome, SeriesIdRecord? record, string catalog)
    {
        if (outcome.Manual is { Reject: true })
        {
            return null;
        }

        return record?.Ids.GetValueOrDefault(catalog) ?? catalog switch
        {
            "wikidata" => ExternalIds.Wikidata(outcome.Facts?.Qid ?? outcome.Article?.Hit.Qid),
            "imdb" => ExternalIds.Imdb(outcome.Facts?.Imdb),
            "tmdb" => ExternalIds.Tmdb(outcome.Facts?.AnyTmdb),
            "tvdb" => ExternalIds.Tvdb(outcome.Facts?.Tvdb),
            _ => null
        };
    }

    /// <summary>
    /// Formats a confidence with a comma, as the rest of the file does.
    /// </summary>
    /// <param name="value">The confidence.</param>
    /// <returns>The confidence, with two decimals.</returns>
    internal static string Confidence(double value)
        => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    /// <summary>
    /// Writes a row of a CSV, quoting every value so the separator never splits a field.
    /// </summary>
    /// <param name="writer">Writer of the file.</param>
    /// <param name="values">Values of the row.</param>
    private static void WriteRow(TextWriter writer, IEnumerable<string?> values)
    {
        var first = true;

        foreach (var value in values)
        {
            if (!first)
            {
                writer.Write(';');
            }

            first = false;
            writer.Write('"');
            writer.Write(value?
                .Replace("\"", "\"\"", StringComparison.Ordinal)
                .Replace('\r', ' ')
                .Replace('\n', ' '));
            writer.Write('"');
        }

        writer.Write("\r\n");
    }
}
