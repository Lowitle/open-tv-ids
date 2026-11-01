using System.Text;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Writes <c>mixnmatch/&lt;emitter&gt;.tsv</c>: the programmes of the catalog in the format the import tool of
/// Mix'n'match reads, so the ones the matcher cannot resolve can be matched by a person in a browser.
/// </summary>
/// <remarks>
/// <para>
/// The import tool recognises the columns <c>id</c>, <c>name</c>, <c>desc</c>, <c>url</c>, <c>q</c> (an item a
/// person already knows to be the right one) and <c>autoq</c> (a good guess, which the tool keeps as an
/// automatic match), and reads any other <c>P####</c> column as the value of a property. The <c>id</c> of a row
/// is the slug of the page of the programme, which is exactly the value of <c>P10356</c>, so the catalog is
/// imported with that property and needs no property column: when a volunteer matches a row, Mix'n'match
/// writes the identifier on the item and the next build publishes the programme without searching its title.
/// </para>
/// <para>
/// The file is tab separated, UTF-8 without byte order mark and with line feeds: the mark would end up inside
/// the name of the first column and the import tool would not recognise it.
/// </para>
/// </remarks>
internal static class MixNMatchSheet
{
    /// <summary>Longest description the import tool keeps, which is the only field it truncates.</summary>
    private const int MaxDescription = 255;

    /// <summary>Columns of the file, in the order the import tool documents them.</summary>
    internal static readonly string[] Columns = ["id", "name", "desc", "url", "q", "autoq"];

    /// <summary>
    /// Builds the rows of the sheet: one per programme of the catalog, with the item the build already knows.
    /// </summary>
    /// <param name="emitter">Emitter of the programmes, which prefixes the global id.</param>
    /// <param name="result">The result of the build.</param>
    /// <param name="log">Sink of the warnings, or <c>null</c>.</param>
    /// <returns>The catalog, ready to be written.</returns>
    /// <remarks>
    /// The sheet holds the whole catalog, not only the programmes that are missing: a volunteer sees what is
    /// already matched (and can correct a wrong match, which is how the two title traps of 3Cat were found)
    /// and the catalog of the emitter is useful in Wikidata as a list of its works.
    /// </remarks>
    internal static MixNMatchCatalog Create(string emitter, BuildResult result, Action<string>? log = null)
    {
        var rows = new List<MixNMatchRow>(result.Outcomes.Count);
        var records = result.Records.ToDictionary(record => record.Id, StringComparer.Ordinal);
        var pages = new Dictionary<string, string>(StringComparer.Ordinal);
        var withoutPage = 0;
        var repeated = 0;

        foreach (var outcome in result.Outcomes)
        {
            // Un programa descartado a mano no es una obra del catálogo: no se le pide nada a nadie.
            if (outcome.Manual is { Reject: true })
            {
                continue;
            }

            var page = ShowIds.PageOf(outcome.Program);
            if (page is null)
            {
                withoutPage++;
                continue;
            }

            // La ficha es la identidad de la fila: dos programas que publican la misma no se pueden casar por
            // separado, así que se queda el primero, que es el que el catálogo sirve antes.
            if (pages.TryGetValue(page, out var kept))
            {
                repeated++;
                log?.Invoke($"Mix'n'match: «{outcome.Program.Title}» publica la ficha de «{kept}» («{page}»); se queda una fila.");

                continue;
            }

            pages[page] = outcome.Program.Title;

            var record = records.GetValueOrDefault($"{emitter}:{outcome.Program.Id}");
            var item = record?.Ids.GetValueOrDefault("wikidata");

            // «q» es «lo sé seguro» y «autoq» es «me lo parece»: el identificador de la ficha y una corrección
            // a mano son certezas, una búsqueda por título es la apuesta que el voluntario tiene que confirmar.
            var certain = item is not null
                && record!.Match.Method is SeriesMatchInfo.IdentifierMethod or SeriesMatchInfo.ManualMethod;

            rows.Add(new MixNMatchRow(
                page,
                outcome.Program.Title,
                Description(outcome.Program),
                outcome.Program.Url ?? string.Empty,
                certain ? item : null,
                certain ? null : item));
        }

        return new MixNMatchCatalog(rows, withoutPage, repeated);
    }

    /// <summary>
    /// Writes the sheet.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="catalog">The rows of the catalog.</param>
    internal static void Write(string path, MixNMatchCatalog catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\n"
        };

        Row(writer, Columns);

        foreach (var row in catalog.Rows)
        {
            Row(writer, [row.Id, row.Name, row.Description, row.Url, row.Certain, row.Guess]);
        }
    }

    /// <summary>
    /// Writes a row of the file, which has no quoting: a tab or a newline inside a value would split the row and
    /// the import would read a column that does not exist.
    /// </summary>
    /// <param name="writer">Writer of the file.</param>
    /// <param name="values">Values of the row.</param>
    private static void Row(TextWriter writer, IEnumerable<string?> values)
        => writer.WriteLine(string.Join('\t', values.Select(Cell)));

    private static string Cell(string? value)
        => value is null
            ? string.Empty
            : value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>
    /// Describes a programme for the volunteer, who has to match a Catalan title with an item of Wikidata and
    /// needs the kind of work and, above all, the title the work has outside the catalog.
    /// </summary>
    /// <param name="program">The programme.</param>
    /// <returns>The description, at most <see cref="MaxDescription"/> characters.</returns>
    private static string Description(ProgramEntry program)
    {
        var category = program.Category?.Trim();
        var original = program.OriginalTitle?.Trim();

        var text = original is { Length: > 0 }
            && !string.Equals(original, program.Title.Trim(), StringComparison.OrdinalIgnoreCase)
                ? category is { Length: > 0 } ? $"{category} · {original}" : original
                : category ?? string.Empty;

        return text.Length <= MaxDescription ? text : text[..MaxDescription];
    }
}

/// <summary>
/// The rows of the sheet of an emitter, and what the sheet had to leave out.
/// </summary>
/// <param name="Rows">One row per programme, in the order of the catalog.</param>
/// <param name="WithoutPage">Programmes without a page, which have no identifier of their own to match.</param>
/// <param name="Repeated">Programmes dropped because another one already publishes the same page.</param>
internal sealed record MixNMatchCatalog(IReadOnlyList<MixNMatchRow> Rows, int WithoutPage, int Repeated)
{
    /// <summary>Gets how many fichas the catalog has.</summary>
    internal int Entries => Rows.Count;

    /// <summary>Gets how many programmes the build matched with evidence Wikidata states.</summary>
    internal int Certain => Rows.Count(row => row.Certain is not null);

    /// <summary>Gets how many programmes the build matched by title, which the volunteer has to confirm.</summary>
    internal int Guess => Rows.Count(row => row.Guess is not null);

    /// <summary>Gets how many programmes are left for a volunteer to match.</summary>
    internal int Pending => Rows.Count(row => !row.Matched);
}

/// <summary>
/// A row of the sheet: a programme of the catalog as the import tool of Mix'n'match reads it.
/// </summary>
/// <param name="Id">Slug of the page of the programme, which is the value of the identifier of the catalog.</param>
/// <param name="Name">Title of the programme in the catalog.</param>
/// <param name="Description">Category and original title, for the person who matches the row.</param>
/// <param name="Url">Page of the programme in the catalog of the emitter.</param>
/// <param name="Certain">Item Wikidata states, which the tool applies as a manual match.</param>
/// <param name="Guess">Item the matcher found by title, which the tool applies as an automatic match.</param>
internal sealed record MixNMatchRow(
    string Id,
    string Name,
    string Description,
    string Url,
    string? Certain,
    string? Guess)
{
    /// <summary>Gets a value indicating whether the build already matched the programme with an item.</summary>
    internal bool Matched => Certain is not null || Guess is not null;
}
