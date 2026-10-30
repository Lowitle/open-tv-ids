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
    /// <summary>Columns of the review file.</summary>
    private static readonly string[] ReviewColumns =
    [
        "id",
        "categoria",
        "tipo",
        "estado",
        "confianza",
        "evidencia",
        "titol",
        "titol_original",
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

        WriteRow(writer, ReviewColumns);

        var records = result.Records.ToDictionary(record => record.Id, StringComparer.Ordinal);

        foreach (var outcome in result.Outcomes)
        {
            var record = records.GetValueOrDefault($"{emitter}:{outcome.Program.Id}");

            WriteRow(writer, [
                outcome.Program.Id,
                outcome.Program.Category,
                outcome.Program.Kind,
                State(outcome),
                Confidence(outcome.Verdict.Confidence),
                outcome.Verdict.Evidence,
                outcome.Program.Title,
                outcome.Program.OriginalTitle,
                outcome.Article?.Hit.Language,
                outcome.Article?.Hit.Title,
                outcome.Article?.Hit.Qid,
                outcome.Article?.Match.ToString(),
                record?.Ids.GetValueOrDefault("wikidata"),
                record?.Ids.GetValueOrDefault("imdb"),
                record?.Ids.GetValueOrDefault("tmdb"),
                record?.Ids.GetValueOrDefault("tvdb"),
                outcome.Suggestion?.Title,
                outcome.Suggestion?.Qid,
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
        => outcome.Manual is not null
            ? "publicado (manual)"
            : outcome.Verdict.State switch
            {
                MatchState.Accepted => "publicado",
                MatchState.Review => "revisar",
                _ => "sin_match"
            };

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
