using System.Text;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Writes <c>report/&lt;emitter&gt;/cobertura.md</c>: what the build found, what it published and what is left
/// for a person to look at.
/// </summary>
/// <remarks>
/// The report is the artefact that decides whether the dataset is worth publishing and what to fix next: a
/// coverage that falls by half is a matching that has broken, not a source without identifiers, and a jump
/// in the review file is the sign of a new trap in the catalog.
/// </remarks>
internal static class CoverageReport
{
    /// <summary>Programmes listed per section, which is enough to see the pattern.</summary>
    private const int Sample = 20;

    /// <summary>Kinds that are works of fiction, which are the ones the dataset cares about most.</summary>
    private static readonly string[] Fiction = ["series", "animation", "anime", "film"];

    /// <summary>
    /// Writes the report.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="result">The result of the build.</param>
    /// <param name="options">Options of the build, which the report quotes.</param>
    /// <param name="today">Day of the build.</param>
    internal static void Write(string path, BuildResult result, Options options, DateOnly today)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var outcomes = result.Outcomes;
        var published = result.Records.Count;
        var publishedIds = result.Records.Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        var manual = outcomes.Count(outcome => outcome.Manual is not null);
        var rejected = outcomes.Count(outcome => outcome.Manual is { Reject: true });
        var review = outcomes.Count(outcome => outcome.Manual is null && outcome.Verdict.State == MatchState.Review);
        var unmatched = outcomes.Count - published - review;
        var text = new StringBuilder();

        text.Append("# Cobertura de identificadores externos · ").Append(options.Emitter).AppendLine();
        text.AppendLine();
        text.Append("Generado el ").Append(Day(today))
            .Append(" a partir de `").Append(Path.GetFileName(options.InputPath))
            .Append("` con `IdsBuilder`.").AppendLine();
        text.AppendLine("Fuente de los identificadores: Wikidata (CC0), leída a través de los artículos de Wikipedia.");
        text.AppendLine();

        text.AppendLine("## Totales");
        text.AppendLine();
        text.AppendLine("| Métrica | Programas | Porcentaje |");
        text.AppendLine("| --- | ---: | ---: |");
        Row(text, "Programas procesados", outcomes.Count, outcomes.Count);
        Row(text, "Publicados en el dataset", published, outcomes.Count);
        Row(text, "En revisión", review, outcomes.Count);
        Row(text, "Sin coincidencia", unmatched, outcomes.Count);

        if (rejected > 0)
        {
            Row(text, "Descartados a mano", rejected, outcomes.Count);
        }

        text.AppendLine();
        text.Append("Umbrales: se publica desde ")
            .Append(Score(options.Accept))
            .Append(" de confianza y se revisa desde ")
            .Append(Score(options.Review))
            .Append(". ")
            .Append(Num(manual))
            .Append(manual == 1 ? " registro viene" : " registros vienen")
            .Append(" de `overrides/").Append(options.Emitter).Append(".json`");

        if (rejected > 0)
        {
            text.Append(", ").Append(Num(rejected))
                .Append(rejected == 1 ? " de ellos descartado" : " de ellos descartados");
        }

        text.AppendLine(".");
        text.AppendLine();

        text.AppendLine("## Identificadores publicados");
        text.AppendLine();
        text.AppendLine("| Catálogo | Programas |");
        text.AppendLine("| --- | ---: |");

        foreach (var (catalog, count) in Identifiers(result.Records))
        {
            text.Append("| ").Append(catalog).Append(" | ").Append(Num(count)).AppendLine(" |");
        }

        text.AppendLine();
        text.AppendLine("## Por categoría");
        text.AppendLine();
        text.AppendLine("| Categoría | Procesados | Publicados | En revisión | Sin coincidencia | Con TMDB |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");

        foreach (var group in outcomes
            .GroupBy(outcome => outcome.Program.Category ?? "(sin categoría)")
            .OrderByDescending(group => group.Count()))
        {
            var inGroup = group.ToList();

            text.Append("| ").Append(Cell(group.Key))
                .Append(" | ").Append(Num(inGroup.Count))
                .Append(" | ").Append(Num(inGroup.Count(outcome => publishedIds.Contains($"{options.Emitter}:{outcome.Program.Id}"))))
                .Append(" | ").Append(Num(inGroup.Count(outcome => outcome.Manual is null && outcome.Verdict.State == MatchState.Review)))
                .Append(" | ").Append(Num(inGroup.Count(outcome => outcome.Manual is null
                    && outcome.Verdict.State == MatchState.Unmatched)))
                .Append(" | ").Append(Num(inGroup.Count(outcome => outcome.Facts is not null
                    && ExternalIds.Tmdb(outcome.Facts.AnyTmdb) is not null)))
                .AppendLine(" |");
        }

        text.AppendLine();
        text.AppendLine("## En revisión");
        text.AppendLine();
        text.Append("Los ").Append(Sample).AppendLine(" programas con más evidencia que no llegan al umbral:");
        text.AppendLine();
        text.AppendLine("| Id | Programa | Confianza | Evidencia | Artículo |");
        text.AppendLine("| --- | --- | ---: | --- | --- |");

        foreach (var outcome in outcomes
            .Where(outcome => outcome.Manual is null && outcome.Verdict.State == MatchState.Review)
            .OrderByDescending(outcome => outcome.Verdict.Confidence)
            .ThenBy(outcome => outcome.Program.Title, StringComparer.Ordinal)
            .Take(Sample))
        {
            text.Append("| ").Append(outcome.Program.Id)
                .Append(" | ").Append(Cell(outcome.Program.Title))
                .Append(" | ").Append(Score(outcome.Verdict.Confidence))
                .Append(" | ").Append(Cell(outcome.Verdict.Evidence))
                .Append(" | ").Append(Article(outcome))
                .AppendLine(" |");
        }

        text.AppendLine();
        text.Append("Para resolverlos de uno en uno, con los enlaces a la ficha del emisor y al elemento de Wikidata: ")
            .Append("`dotnet run --project src/IdsBuilder -- --check` (guarda cada decisión en ")
            .Append($"`overrides/{options.Emitter}.json`")
            .AppendLine(").");

        text.AppendLine();
        text.AppendLine("## Ficción sin coincidencia");
        text.AppendLine();
        text.Append("Series, animación y cine que no se han podido casar (primeros ").Append(Sample)
            .AppendLine(" por antigüedad del id):");
        text.AppendLine();
        text.AppendLine("| Id | Programa | Título original | Sugerencia |");
        text.AppendLine("| --- | --- | --- | --- |");

        foreach (var outcome in outcomes
            .Where(outcome => outcome.Manual is null
                && outcome.Verdict.State == MatchState.Unmatched
                && Fiction.Contains(outcome.Program.Kind))
            .OrderBy(outcome => outcome.Program.Id.Length)
            .ThenBy(outcome => outcome.Program.Id, StringComparer.Ordinal)
            .Take(Sample))
        {
            text.Append("| ").Append(outcome.Program.Id)
                .Append(" | ").Append(Cell(outcome.Program.Title))
                .Append(" | ").Append(Cell(outcome.Program.OriginalTitle))
                .Append(" | ").Append(outcome.Suggestion is null
                    ? "—"
                    : $"{Cell(outcome.Suggestion.Title)} ({outcome.Suggestion.Language})")
                .AppendLine(" |");
        }

        text.AppendLine();
        text.AppendLine("## Elementos compartidos");
        text.AppendLine();

        if (result.Duplicates.Count == 0)
        {
            text.AppendLine("Ninguno: cada elemento de Wikidata se publica una sola vez.");
        }
        else
        {
            text.AppendLine("Elementos de Wikidata que publican varios programas. Puede ser legítimo (una serie y su resumen) o un error de casación:");
            text.AppendLine();
            text.AppendLine("| Elemento | Programas |");
            text.AppendLine("| --- | --- |");

            foreach (var duplicate in result.Duplicates.Take(Sample))
            {
                var parts = duplicate.Split(": ", 2);
                text.Append("| ").Append(parts[0])
                    .Append(" | ").Append(Cell(parts.Length > 1 ? parts[1] : duplicate))
                    .AppendLine(" |");
            }
        }

        text.AppendLine();
        text.AppendLine("## Cómo se decide");
        text.AppendLine();
        text.AppendLine("El título del catálogo se busca en la Wikipedia catalana; si no aparece, el título original se busca en la inglesa y luego en la española. Se puntúa cada uno de los tres primeros artículos candidatos y gana el que más suma:");
        text.AppendLine();
        text.AppendLine("- título idéntico al del artículo (o solo con diferencia de artículo: «Capitana Marleau» y «La capitana Marleau»): **0,60**;");
        text.AppendLine("- artículo con el título más un calificador («Merlí (sèrie de televisió)»): **0,55**;");
        text.AppendLine("- un título contiene al otro («Crims» y «Crims a Barcelona»): **0,40**;");
        text.AppendLine("- el calificador nombra una obra o un año («(programa)», «(2005 TV series)»): **+0,05**;");
        text.AppendLine("- identificador de TMDB o TVDB del elemento: **+0,20** (solo IMDb, **+0,10**);");
        text.AppendLine("- el elemento es una serie de televisión (`P31/P279*` de Q5398426): **+0,10**;");
        text.AppendLine("- el año de inicio (`P580`) coincide con el del catálogo: **+0,10**.");
        text.AppendLine();
        text.AppendLine("Un año que no coincide no resta: la fecha del catálogo es la de alta en 3Cat, no la de estreno.");
        text.AppendLine();
        text.Append("Tiempo del volcado: ").Append(Elapsed(result.Elapsed)).AppendLine(".");
        text.AppendLine();

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// Counts the records that publish each catalog.
    /// </summary>
    /// <param name="records">The records of the dataset.</param>
    /// <returns>The count of each catalog, most used first.</returns>
    internal static List<KeyValuePair<string, int>> Identifiers(IReadOnlyList<SeriesIdRecord> records)
        => records
            .SelectMany(record => record.Ids.Keys)
            .GroupBy(catalog => catalog, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

    private static void Row(StringBuilder text, string label, int value, int total)
        => text.Append("| ").Append(label).Append(" | ").Append(Num(value)).Append(" | ").Append(Pct(value, total)).AppendLine(" |");

    private static string Article(Outcome outcome)
        => outcome.Article is null
            ? "—"
            : $"{Cell(outcome.Article.Hit.Title)} ({outcome.Article.Hit.Language}, {outcome.Article.Hit.Qid})";

    private static string Cell(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? "—"
            : value.Replace("|", "\\|", StringComparison.Ordinal).Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string Score(double value)
        => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');

    private static string Day(DateOnly day)
        => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string Elapsed(TimeSpan elapsed)
        => $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";

    private static string Num(int value)
        => value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');

    private static string Pct(int value, int total)
        => total == 0
            ? "—"
            : (100.0 * value / total).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " %";
}

