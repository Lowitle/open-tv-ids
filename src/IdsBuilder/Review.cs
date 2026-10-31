using System.Text;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// What a person answers when the review asks about a programme.
/// </summary>
internal enum ReviewAction
{
    /// <summary>Nothing decided: the programme stays as it is.</summary>
    Skip,

    /// <summary>The match is the work: publish its identifiers.</summary>
    Accept,

    /// <summary>The match is the work and a second source confirms it.</summary>
    Verify,

    /// <summary>The match is not the work: keep the programme out of the dataset.</summary>
    Reject,

    /// <summary>The match is wrong but the item of the work is known.</summary>
    Other,

    /// <summary>The person wants the keys again.</summary>
    Help,

    /// <summary>Stop the review; what is decided is already saved.</summary>
    Quit,

    /// <summary>The answer is none of the above.</summary>
    Unknown
}

/// <summary>
/// An answer of the review, with the argument when the answer needs one.
/// </summary>
/// <param name="Action">What the person wants.</param>
/// <param name="Value">The item of the work, for <see cref="ReviewAction.Other"/>.</param>
internal sealed record ReviewAnswer(ReviewAction Action, string? Value = null)
{
    /// <summary>Gets the answer that leaves the programme for later.</summary>
    internal static ReviewAnswer Skip { get; } = new(ReviewAction.Skip);

    /// <summary>Gets the answer that publishes the identifiers of the row.</summary>
    internal static ReviewAnswer Accept { get; } = new(ReviewAction.Accept);

    /// <summary>Gets the answer that publishes them as verified.</summary>
    internal static ReviewAnswer Verify { get; } = new(ReviewAction.Verify);

    /// <summary>Gets the answer that discards the programme.</summary>
    internal static ReviewAnswer Reject { get; } = new(ReviewAction.Reject);

    /// <summary>Gets the answer that stops the review.</summary>
    internal static ReviewAnswer Quit { get; } = new(ReviewAction.Quit);

    /// <summary>Gets the answer that asks for the keys.</summary>
    internal static ReviewAnswer Help { get; } = new(ReviewAction.Help);

    /// <summary>Gets an answer the review does not understand.</summary>
    internal static ReviewAnswer Unknown { get; } = new(ReviewAction.Unknown);
}

/// <summary>
/// Reads what a person types in the review.
/// </summary>
/// <remarks>
/// The answers are single letters, so the review is one keystroke and Enter per programme, and every letter
/// has a word as well: a wrapper or a terminal that eats a letter is easier to survive than an unlabelled
/// key. A bare item id (<c>Q2688944</c>) or the URL of the item counts as <see cref="ReviewAction.Other"/>,
/// which is what makes pasting from a browser work.
/// </remarks>
internal static class ReviewAnswers
{
    /// <summary>
    /// Reads an answer.
    /// </summary>
    /// <param name="line">The line the person typed; <c>null</c> or empty to leave it for later.</param>
    /// <returns>The answer.</returns>
    internal static ReviewAnswer Parse(string? line)
    {
        var text = Clean(line);

        switch (text.ToLowerInvariant())
        {
            case "":
            case "d":
            case "despues":
            case "después":
            case "siguiente":
                return ReviewAnswer.Skip;
            case "s":
            case "si":
            case "sí":
            case "y":
            case "ok":
                return ReviewAnswer.Accept;
            case "v":
            case "verificado":
                return ReviewAnswer.Verify;
            case "n":
            case "no":
            case "x":
                return ReviewAnswer.Reject;
            case "o":
            case "otra":
            case "otro":
                return new ReviewAnswer(ReviewAction.Other);
            case "q":
            case "quit":
            case "salir":
                return ReviewAnswer.Quit;
            case "?":
            case "a":
            case "ayuda":
            case "help":
                return ReviewAnswer.Help;
        }

        if (Item(text) is { } item)
        {
            return new ReviewAnswer(ReviewAction.Other, item);
        }

        if (text.Length > 2 && text[0] is 'o' or 'O' && text[1] is ' ' or ':')
        {
            return Item(text[2..]) is { } typed
                ? new ReviewAnswer(ReviewAction.Other, typed)
                : ReviewAnswer.Unknown;
        }

        return ReviewAnswer.Unknown;
    }

    /// <summary>
    /// Reads the item of a work from what a person typed, which may be the id or the URL of the page.
    /// </summary>
    /// <param name="value">The text, with or without the URL of Wikidata.</param>
    /// <returns>The item (<c>Q2688944</c>), or <c>null</c> when the text is not an item.</returns>
    internal static string? Item(string? value)
    {
        var text = Clean(value);
        if (text.Length == 0)
        {
            return null;
        }

        var slash = text.LastIndexOf('/');
        if (slash >= 0 && slash + 1 < text.Length)
        {
            text = text[(slash + 1)..];
        }

        return ExternalIds.Wikidata(text[0] is 'q' ? "Q" + text[1..] : text);
    }

    /// <summary>
    /// Reads a line of a person, which may arrive with the marks of a redirected stream (a byte order mark,
    /// a null character) when the answers are piped from a file or from a script.
    /// </summary>
    /// <param name="value">The line.</param>
    /// <returns>The line without control characters, trimmed.</returns>
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (!char.IsControl(character) && character != '\uFEFF' && character != '\uFFFD')
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }
}

/// <summary>
/// Turns an answer of the review into the correction of <c>overrides/&lt;emitter&gt;.json</c>.
/// </summary>
/// <remarks>
/// The correction always carries every identifier the person confirmed, because an override replaces the
/// ones the matcher found: accepting the row of a programme publishes exactly what the review file showed,
/// and pointing at another item publishes what that item has, not a mixture of both.
/// </remarks>
internal static class ReviewDecision
{
    /// <summary>
    /// Builds the correction that publishes the identifiers of a row.
    /// </summary>
    /// <param name="row">The programme.</param>
    /// <param name="verified">Whether a second source confirms the identifiers.</param>
    /// <returns>The correction.</returns>
    internal static ManualOverride Accept(RevisionRow row, bool verified)
        => new()
        {
            Id = row.Id,
            Ids = row.Identifiers(),
            Note = verified ? "revisado a mano, comprobado en la ficha" : "revisado a mano",
            Verified = verified
        };

    /// <summary>
    /// Builds the correction that keeps a programme out of the dataset.
    /// </summary>
    /// <param name="row">The programme.</param>
    /// <returns>The correction.</returns>
    internal static ManualOverride Reject(RevisionRow row)
        => new()
        {
            Id = row.Id,
            Note = "descartado a mano: el emparejamiento no era la obra",
            Reject = true
        };

    /// <summary>
    /// Builds the correction that points a programme at the item of the work.
    /// </summary>
    /// <param name="row">The programme.</param>
    /// <param name="item">The item (<c>Q2688944</c>).</param>
    /// <param name="facts">The item of Wikidata, when it could be read.</param>
    /// <returns>The correction.</returns>
    internal static ManualOverride Other(RevisionRow row, string item, EntityFacts? facts)
    {
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);

        if (ExternalIds.Wikidata(item) is { } qid)
        {
            ids["wikidata"] = qid;
        }

        if (ExternalIds.Imdb(facts?.Imdb) is { } imdb)
        {
            ids["imdb"] = imdb;
        }

        if (ExternalIds.Tmdb(facts?.AnyTmdb) is { } tmdb)
        {
            ids["tmdb"] = tmdb;
        }

        if (ExternalIds.Tvdb(facts?.Tvdb) is { } tvdb)
        {
            ids["tvdb"] = tvdb;
        }

        return new()
        {
            Id = row.Id,
            Ids = ids,
            Note = facts is null
                ? $"corregido a mano a {item}"
                : $"corregido a mano a {item}, con los identificadores del elemento",
            Verified = facts is not null
        };
    }
}

/// <summary>
/// Runs the review of <c>report/&lt;emitter&gt;/revision.csv</c>: one programme at a time, with the page of the
/// emitter and the item of Wikidata at hand, and every answer saved as soon as it is given.
/// </summary>
/// <remarks>
/// <para>
/// The review is a conversation and not a batch because a person reads in bursts: he sits down for ten
/// minutes, decides a handful of programmes and closes the terminal. Saving after every answer is what makes
/// that possible without a second file to keep in sync, and the corrections live in
/// <c>overrides/&lt;emitter&gt;.json</c>, which the next build applies.
/// </para>
/// <para>
/// The review does not reach the network by itself: the only request it makes is the one of an item a person
/// asked about, through the resolver the caller passes, so a session with <c>--offline</c> works the same.
/// </para>
/// </remarks>
/// <param name="options">Options of the build, for the paths and the emitter.</param>
/// <param name="input">Where the answers come from.</param>
/// <param name="output">Where the questions go.</param>
/// <param name="resolve">Reads an item of Wikidata, or <c>null</c> when the review cannot ask.</param>
internal sealed class ReviewSession(
    Options options,
    TextReader input,
    TextWriter output,
    Func<string, CancellationToken, Task<EntityFacts?>>? resolve)
{
    private const string Indent = "   ";

    private readonly Dictionary<string, ManualOverride> _overrides =
        Overrides.Read(options.OverridesPath, message => output.WriteLine(message));

    /// <summary>
    /// Reviews the programmes that are pending.
    /// </summary>
    /// <param name="rows">The rows of the review file.</param>
    /// <param name="cancellationToken">Token to stop the review.</param>
    /// <returns>0 when the review ended, which is also the case when there was nothing to review.</returns>
    internal async Task<int> RunAsync(IReadOnlyList<RevisionRow> rows, CancellationToken cancellationToken)
    {
        var pending = Queue(rows);
        Header(rows.Count, pending.Count);

        if (pending.Count == 0)
        {
            output.WriteLine("No queda nada que revisar: --all añade los programas sin coincidencia y --again");
            output.WriteLine("vuelve a los que ya tienen una corrección.");

            return 0;
        }

        var accepted = 0;
        var rejected = 0;

        for (var index = 0; index < pending.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = pending[index];
            var (action, manual) = await AskAsync(row, index + 1, pending.Count, cancellationToken)
                .ConfigureAwait(false);

            if (action == ReviewAction.Quit)
            {
                break;
            }

            if (manual is null)
            {
                continue;
            }

            _overrides[row.Id] = manual;
            Overrides.Write(options.OverridesPath, _overrides);

            if (manual.Reject)
            {
                rejected++;
            }
            else
            {
                accepted++;
            }

            output.WriteLine(manual.Reject ? $"{Indent}→ descartado y guardado." : $"{Indent}→ guardado.");
        }

        Summary(pending.Count, accepted, rejected);

        return 0;
    }

    /// <summary>
    /// Builds the list of programmes the review asks about, most promising first, so a session of ten minutes
    /// decides the programmes that are easiest to confirm.
    /// </summary>
    /// <param name="rows">The rows of the review file.</param>
    /// <returns>The programmes to ask about.</returns>
    private List<RevisionRow> Queue(IReadOnlyList<RevisionRow> rows)
        => rows
            .Where(row => row.NeedsReview || (options.CheckAll && row.Unmatched))
            .Where(row => options.CheckAgain || !_overrides.ContainsKey(row.Id))
            .OrderByDescending(row => row.Confidence)
            .ThenBy(row => row.Id, StringComparer.Ordinal)
            .ToList();

    private void Header(int total, int pending)
    {
        output.WriteLine();
        output.WriteLine($"{options.Emitter} · revisión a mano de {Path.GetFileName(options.ReviewPath)}");
        output.WriteLine($"{pending} por revisar de {total} · {_overrides.Count} correcciones ya guardadas en "
            + Path.GetFileName(options.OverridesPath));
        output.WriteLine();
        Keys();
    }

    private void Keys()
    {
        output.WriteLine("  s  es la misma obra");
        output.WriteLine("  v  es la misma obra y lo he comprobado en la ficha");
        output.WriteLine("  n  no es esta obra: no se publica");
        output.WriteLine("  o  otra ficha (Q… o el enlace de Wikidata)");
        output.WriteLine("  d  dejarlo para luego        q  salir        ?  esta ayuda");
        output.WriteLine("Cada respuesta se guarda al momento; la revisión se puede dejar a medias.");
        output.WriteLine();
    }

    private void Card(RevisionRow row, int number, int total)
    {
        var title = row.OriginalTitle is null ? row.Title : $"{row.Title} ({row.OriginalTitle})";
        var ids = row.Identifiers();

        output.WriteLine();
        output.WriteLine($"[{number}/{total}] {row.Id} · {row.Category ?? "sin categoría"} · {row.State} · "
            + $"confianza {DatasetWriter.Confidence(row.Confidence)} · {row.Evidence ?? "sin evidencia"}");
        output.WriteLine($"{Indent}Título:   {title}");

        if (row.ArticleTitle is not null)
        {
            output.WriteLine($"{Indent}Artículo: {row.ArticleLanguage}:{row.ArticleTitle} "
                + $"({row.Match}) · {row.ArticleQid}");
        }

        output.WriteLine(ids.Count == 0
            ? $"{Indent}Ids:      ninguno"
            : $"{Indent}Ids:      {string.Join(" · ", ids.Select(pair => $"{pair.Key} {pair.Value}"))}");

        if (row.Suggestion is not null && row.SuggestionQid != row.ArticleQid)
        {
            output.WriteLine($"{Indent}Pista:    {row.Suggestion} · {Urls.Wikidata(row.SuggestionQid)}");
        }

        if (row.Note is not null)
        {
            output.WriteLine($"{Indent}Nota:     {row.Note}");
        }

        var links = new List<string>(4);
        Add(links, row.ProgramUrl);
        Add(links, Urls.Wikidata(row.Wikidata ?? row.ArticleQid));
        Add(links, Urls.Wikipedia(row.ArticleLanguage, row.ArticleTitle));
        Add(links, Urls.Imdb(row.Imdb));

        for (var index = 0; index < links.Count; index++)
        {
            output.WriteLine($"{Indent}{(index == 0 ? "Abrir:    " : new string(' ', 10))}{links[index]}");
        }
    }

    private async Task<(ReviewAction Action, ManualOverride? Manual)> AskAsync(
        RevisionRow row,
        int number,
        int total,
        CancellationToken cancellationToken)
    {
        Card(row, number, total);

        while (true)
        {
            output.Write($"{Indent}¿Es la misma obra? [s/v/n/o/d/q/?] > ");

            var line = input.ReadLine();
            if (line is null)
            {
                output.WriteLine();

                return (ReviewAction.Quit, null);
            }

            var answer = ReviewAnswers.Parse(line);

            switch (answer.Action)
            {
                case ReviewAction.Accept:
                case ReviewAction.Verify:
                    if (row.Identifiers().Count == 0)
                    {
                        output.WriteLine($"{Indent}Esta fila no tiene ningún identificador que aceptar: usa «o» "
                            + "para poner la ficha de la obra.");
                        continue;
                    }

                    return (answer.Action, ReviewDecision.Accept(row, answer.Action == ReviewAction.Verify));
                case ReviewAction.Reject:
                    return (answer.Action, ReviewDecision.Reject(row));
                case ReviewAction.Skip:
                case ReviewAction.Quit:
                    return (answer.Action, null);
                case ReviewAction.Help:
                    Keys();
                    continue;
                case ReviewAction.Other:
                    if (await OtherAsync(row, answer.Value, cancellationToken).ConfigureAwait(false) is { } other)
                    {
                        return (answer.Action, other);
                    }

                    continue;
                default:
                    output.WriteLine($"{Indent}No te he entendido: s, v, n, o, d, q o ?.");
                    continue;
            }
        }
    }

    private async Task<ManualOverride?> OtherAsync(
        RevisionRow row,
        string? item,
        CancellationToken cancellationToken)
    {
        var qid = item;

        if (qid is null)
        {
            output.Write($"{Indent}Ficha de la obra (Q… o el enlace de Wikidata), Enter para volver > ");

            var line = input.ReadLine();
            if (line is null)
            {
                return null;
            }

            qid = ReviewAnswers.Item(line);
        }

        if (qid is null)
        {
            output.WriteLine($"{Indent}No es un id de Wikidata: escribe algo como Q2688944.");

            return null;
        }

        EntityFacts? facts = null;

        if (resolve is not null)
        {
            try
            {
                facts = await resolve(qid, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                output.WriteLine($"{Indent}No se pudo leer {qid}: {ex.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                output.WriteLine($"{Indent}No se pudo leer {qid}: la petición no contestó.");
            }
        }

        output.WriteLine(facts is null
            ? $"{Indent}{qid} · solo se guarda el id"
            : $"{Indent}{qid} · {Urls.Wikidata(qid)}");

        return ReviewDecision.Other(row, qid, facts);
    }

    private void Summary(int pending, int accepted, int rejected)
    {
        output.WriteLine();

        var decided = accepted + rejected;

        if (decided == 0)
        {
            output.WriteLine("Nada decidido.");

            return;
        }

        output.WriteLine($"Decididos {decided}: {accepted} {Plural(accepted, "aceptado")} y "
            + $"{rejected} {Plural(rejected, "descartado")}. Quedan {pending - decided} de {pending}.");
        output.WriteLine($"Guardado en {Path.GetFullPath(options.OverridesPath)}. Para aplicarlo al dataset:");
        output.WriteLine($"   dotnet run --project src/IdsBuilder -- --emitter {options.Emitter} --offline");
    }

    private static string Plural(int count, string word) => count == 1 ? word : word + "s";

    private static void Add(List<string> links, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            links.Add(url);
        }
    }
}
