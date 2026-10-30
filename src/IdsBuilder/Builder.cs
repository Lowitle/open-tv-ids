using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using OpenTvIds.IdsBuilder.Emitters;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// What the lookup decided about a programme.
/// </summary>
/// <param name="Program">The programme.</param>
/// <param name="Candidates">
/// The articles whose title matches the title of the programme, at most
/// <see cref="Builder.MaxCandidates"/>. The first one is the most promising, but the scorer decides: the
/// article of «Polònia» that comes first in a search is the country, and «Polònia (programa)» comes after
/// it.
/// </param>
/// <param name="Suggestion">The best article of a search that did not match, for the review file.</param>
/// <param name="SuggestionMatch">How well the suggestion matches, which is usually <see cref="TitleMatch.None"/>.</param>
internal sealed record Resolution(
    ProgramEntry Program,
    IReadOnlyList<ArticleMatch> Candidates,
    ArticleHit? Suggestion,
    TitleMatch SuggestionMatch);

/// <summary>
/// One programme, with everything the builder knows about it.
/// </summary>
/// <param name="Program">The programme.</param>
/// <param name="Article">The article the matcher chose.</param>
/// <param name="Suggestion">The best article of a search that did not match.</param>
/// <param name="SuggestionMatch">How well the suggestion matches.</param>
/// <param name="Facts">The item of the article, when it could be read.</param>
/// <param name="Verdict">The verdict of the scorer.</param>
/// <param name="Manual">The override of the review file, when a person wrote one.</param>
internal sealed record Outcome(
    ProgramEntry Program,
    ArticleMatch? Article,
    ArticleHit? Suggestion,
    TitleMatch SuggestionMatch,
    EntityFacts? Facts,
    MatchVerdict Verdict,
    ManualOverride? Manual);

/// <summary>
/// The result of a build: every programme with its verdict, the records that are published, and how long
/// the build took.
/// </summary>
/// <param name="Outcomes">Every programme, in the order of the catalog.</param>
/// <param name="Records">The records of the dataset, sorted by id.</param>
/// <param name="Duplicates">Items that more than one record publishes, which is a sign of a wrong match.</param>
/// <param name="Elapsed">How long the build took.</param>
internal sealed record BuildResult(
    IReadOnlyList<Outcome> Outcomes,
    IReadOnlyList<SeriesIdRecord> Records,
    IReadOnlyList<string> Duplicates,
    TimeSpan Elapsed);

/// <summary>
/// Matches the programmes of an emitter with Wikidata and writes the dataset.
/// </summary>
/// <remarks>
/// <para>
/// The build has four steps. The first one looks every programme up in a Wikipedia, which answers with the
/// article and the Wikidata item of the work in a single request. The second one reads the identifiers of
/// the items fifty at a time. The third one scores every programme and decides what to publish, to review
/// or to leave unmatched. The fourth one writes the dataset, the review file and the coverage report.
/// </para>
/// <para>
/// The first and the second steps are the only ones that reach the network, and both go through the cache,
/// so a second build after a change of the catalog only asks for the programmes whose title changed.
/// </para>
/// </remarks>
internal sealed class Builder
{
    /// <summary>
    /// Articles of a programme that are read from Wikidata. The search of a title answers with the country,
    /// the film and the series of the same name, and only the identifiers tell them apart, so a couple of
    /// candidates are worth the query; more of them start to read the whole disambiguation page.
    /// </summary>
    internal const int MaxCandidates = 3;

    private readonly Options _options;
    private readonly IProgramCatalog _catalog;
    private readonly Cache _cache;
    private readonly WikidataClient _wikidata;
    private readonly Action<string> _progress;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, ManualOverride> _overrides;

    /// <summary>
    /// Initializes a new instance of the <see cref="Builder"/> class.
    /// </summary>
    /// <param name="options">Options of the build.</param>
    /// <param name="catalog">Source of the programmes of the emitter.</param>
    /// <param name="cache">Cache of the answers of the endpoints.</param>
    /// <param name="wikidata">Client of Wikipedia and Wikidata.</param>
    /// <param name="overrides">Manual overrides, keyed by the global id of the record.</param>
    /// <param name="progress">Sink of the progress messages.</param>
    /// <param name="log">Sink of the warnings, or <c>null</c>.</param>
    internal Builder(
        Options options,
        IProgramCatalog catalog,
        Cache cache,
        WikidataClient wikidata,
        Dictionary<string, ManualOverride> overrides,
        Action<string> progress,
        Action<string>? log)
    {
        _options = options;
        _catalog = catalog;
        _cache = cache;
        _wikidata = wikidata;
        _overrides = overrides;
        _progress = progress;
        _log = log;
    }

    /// <summary>
    /// Builds the dataset.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the build.</returns>
    internal async Task<BuildResult> RunAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var programs = _catalog.Read(_options.IncludeWeb).ToList();

        if (_options.Skip > 0)
        {
            programs = programs.Skip(_options.Skip).ToList();
        }

        if (_options.Limit > 0 && programs.Count > _options.Limit)
        {
            programs = programs.Take(_options.Limit).ToList();
        }

        _progress($"{_catalog.Emitter}: {programs.Count} programas para casar con Wikidata.");

        var resolutions = await ResolveAllAsync(programs, cancellationToken).ConfigureAwait(false);
        var facts = await ReadFactsAsync(resolutions.Values, cancellationToken).ConfigureAwait(false);

        return Score(programs, resolutions, facts, started.Elapsed);
    }

    /// <summary>
    /// Looks every programme up in the Wikipedias.
    /// </summary>
    private async Task<Dictionary<string, Resolution>> ResolveAllAsync(
        IReadOnlyList<ProgramEntry> programs,
        CancellationToken cancellationToken)
    {
        var resolutions = new ConcurrentDictionary<string, Resolution>(StringComparer.Ordinal);
        var done = 0;
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(_options.Concurrency, 1, 8),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(
            programs,
            parallel,
            async (program, token) =>
            {
                resolutions[program.Id] = await ResolveAsync(program, token).ConfigureAwait(false);

                var count = Interlocked.Increment(ref done);
                if (count % 100 == 0 || count == programs.Count)
                {
                    _progress($"   {count}/{programs.Count} programas consultados.");
                }
            }).ConfigureAwait(false);

        return new Dictionary<string, Resolution>(resolutions, StringComparer.Ordinal);
    }

    /// <summary>
    /// Looks a programme up, title by title, and keeps the articles whose title matches.
    /// </summary>
    /// <remarks>
    /// Every article that matches is kept, not only the best one, because the order of a search is the
    /// order of its relevance and not the order of the usefulness of the article: the search of «Polònia»
    /// answers with the country, «Polònia (programa)» is the third result, and the search of «Merlí»
    /// answers with the page of the disambiguation of the name. The scorer reads the identifiers of every
    /// candidate, and the one that is a television series with a TMDB id wins.
    /// </remarks>
    private async Task<Resolution> ResolveAsync(ProgramEntry program, CancellationToken cancellationToken)
    {
        var candidates = new List<ArticleMatch>(MaxCandidates);
        ArticleHit? suggestion = null;
        var suggestionMatch = TitleMatch.None;

        foreach (var lookup in program.Lookups())
        {
            var hits = await SearchAsync(lookup, cancellationToken).ConfigureAwait(false);

            var ranked = hits
                .Select(hit => (Hit: hit, Match: CrossIdTitle.Compare(lookup.Title, hit.Title)))
                .Where(candidate => candidate.Match != TitleMatch.None)
                .OrderByDescending(candidate => candidate.Match)
                .ThenBy(candidate => candidate.Hit.Index)
                .ToList();

            foreach (var (hit, match) in ranked)
            {
                if (candidates.Count < MaxCandidates && candidates.All(candidate => candidate.Hit.Qid != hit.Qid))
                {
                    candidates.Add(new ArticleMatch(hit, match, lookup.Title));
                }
            }

            if (candidates.Count >= MaxCandidates)
            {
                break;
            }

            if (suggestion is null && hits.Count > 0)
            {
                // Nothing matches, but the answer of the first search that found something is what a
                // person needs to confirm or deny the programme by hand in the review file.
                suggestion = hits[0];
                suggestionMatch = CrossIdTitle.Compare(lookup.Title, hits[0].Title);
            }
        }

        // The most promising candidate first: the title match, then the position of the search.
        candidates.Sort((left, right) => right.Match != left.Match
            ? right.Match.CompareTo(left.Match)
            : left.Hit.Index.CompareTo(right.Hit.Index));

        return new Resolution(program, candidates, suggestion, suggestionMatch);
    }

    /// <summary>
    /// Searches a title, through the cache.
    /// </summary>
    private async Task<IReadOnlyList<ArticleHit>> SearchAsync(LookupTitle lookup, CancellationToken cancellationToken)
    {
        var key = $"{lookup.Language}:{lookup.Title}";

        if (_cache.TryGet("search", key, out List<ArticleHit>? cached) && cached is not null)
        {
            return cached;
        }

        var hits = await _wikidata.SearchAsync(lookup.Language, lookup.Title, cancellationToken).ConfigureAwait(false);
        if (hits is null)
        {
            // A failed request is not an answer: it must not be cached, or the next build would keep the
            // gap of the network forever.
            return [];
        }

        _cache.Set("search", key, hits.ToList());

        return hits;
    }

    /// <summary>
    /// Reads the identifiers of the items that the searches found, through the cache.
    /// </summary>
    private async Task<Dictionary<string, EntityFacts>> ReadFactsAsync(
        IEnumerable<Resolution> resolutions,
        CancellationToken cancellationToken)
    {
        var facts = new Dictionary<string, EntityFacts>(StringComparer.Ordinal);
        var pending = new List<string>();

        foreach (var qid in resolutions
            .SelectMany(resolution => resolution.Candidates.Select(candidate => candidate.Hit.Qid))
            .Distinct(StringComparer.Ordinal))
        {
            if (_cache.TryGet("entities", qid, out EntityFacts? cached) && cached is not null)
            {
                facts[qid] = cached;
            }
            else
            {
                pending.Add(qid);
            }
        }

        foreach (var batch in pending.Chunk(WikidataClient.ItemsPerQuery))
        {
            var answer = await _wikidata.GetFactsAsync(batch, cancellationToken).ConfigureAwait(false);

            foreach (var (qid, item) in answer)
            {
                facts[qid] = item;
                _cache.Set("entities", qid, item);
            }
        }

        if (pending.Count > 0)
        {
            _progress($"   {facts.Count} elementos de Wikidata leídos.");
        }

        return facts;
    }

    /// <summary>
    /// Scores every programme and builds the records that are published.
    /// </summary>
    /// <param name="programs">The programmes of the catalog.</param>
    /// <param name="resolutions">What the lookup decided about each one.</param>
    /// <param name="facts">The items of the articles.</param>
    /// <param name="elapsed">Time spent so far.</param>
    /// <returns>The result of the build.</returns>
    private BuildResult Score(
        IReadOnlyList<ProgramEntry> programs,
        Dictionary<string, Resolution> resolutions,
        Dictionary<string, EntityFacts> facts,
        TimeSpan elapsed)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var firstSeen = ReadFirstSeen();
        var outcomes = new List<Outcome>(programs.Count);
        var records = new List<SeriesIdRecord>(programs.Count);

        foreach (var program in programs)
        {
            var resolution = resolutions[program.Id];
            var scored = resolution.Candidates
                .Select(candidate =>
                {
                    var candidateFacts = facts.GetValueOrDefault(candidate.Hit.Qid);

                    return new Scored(
                        candidate,
                        candidateFacts,
                        MatchScorer.Evaluate(program, candidate, candidateFacts, _options.Accept, _options.Review));
                })
                .OrderByDescending(candidate => candidate.Verdict.Confidence)
                .ThenByDescending(candidate => candidate.Facts?.HasAnyId ?? false)
                .ThenBy(candidate => candidate.Article.Hit.Index)
                .FirstOrDefault();

            var article = scored?.Article;
            var item = scored?.Facts;
            var verdict = scored?.Verdict ?? MatchScorer.Evaluate(program, null, null, _options.Accept, _options.Review);
            var manual = _overrides.GetValueOrDefault($"{_options.Emitter}:{program.Id}");

            outcomes.Add(new Outcome(program, article, resolution.Suggestion, resolution.SuggestionMatch, item, verdict, manual));

            if (verdict.State != MatchState.Accepted && manual is null)
            {
                continue;
            }

            var record = SeriesIdRecord.Create(_options.Emitter, program, article, item, verdict, today);
            if (manual is not null)
            {
                Overrides.Apply(record, manual);
            }

            if (record.Ids.Count == 0)
            {
                continue;
            }

            if (firstSeen.TryGetValue(record.Id, out var seen))
            {
                record.FirstSeen = seen;
            }

            record.LastSeen = today;
            records.Add(record);
        }

        return new BuildResult(outcomes, Sorted(records), Duplicates(records), elapsed);
    }

    private static List<SeriesIdRecord> Sorted(List<SeriesIdRecord> records)
        => records
            .OrderBy(record => long.TryParse(record.EmitterId, out var id) ? id : long.MaxValue)
            .ThenBy(record => record.Id, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Finds the items that more than one record publishes.
    /// </summary>
    /// <remarks>
    /// Two programmes of 3Cat can be the same work (a series and its «resum»), but they can also be the
    /// sign of a wrong match: a film and the series it belongs to share the title but not the item, and the
    /// matcher may have picked the same one for both. The report lists them so a person can look.
    /// </remarks>
    private static List<string> Duplicates(List<SeriesIdRecord> records)
        => records
            .Where(record => record.Ids.ContainsKey("wikidata"))
            .GroupBy(record => record.Ids["wikidata"], StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderByDescending(group => group.Count())
            .Select(group => $"{group.Key}: {string.Join(" · ", group.Select(record => record.Title))}")
            .ToList();

    /// <summary>
    /// Reads the day each record was published for the first time, so a rebuild keeps the original date.
    /// </summary>
    /// <returns>The first day of each record, keyed by the global id.</returns>
    private Dictionary<string, DateOnly> ReadFirstSeen()
    {
        var firstSeen = new Dictionary<string, DateOnly>(StringComparer.Ordinal);

        if (!File.Exists(_options.DatasetPath))
        {
            return firstSeen;
        }

        foreach (var line in File.ReadLines(_options.DatasetPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<SeriesIdRecord>(line, DatasetJson.Options);
                if (record?.FirstSeen is { } seen && !string.IsNullOrEmpty(record.Id))
                {
                    firstSeen[record.Id] = seen;
                }
            }
            catch (JsonException ex)
            {
                _log?.Invoke($"Línea ilegible en {_options.DatasetPath}: {ex.Message}");
            }
        }

        return firstSeen;
    }

    /// <summary>
    /// A candidate of a programme, with its item and the verdict of the scorer.
    /// </summary>
    /// <param name="Article">The article of the candidate.</param>
    /// <param name="Facts">The item of the article, when it could be read.</param>
    /// <param name="Verdict">The verdict of the scorer for this candidate.</param>
    private sealed record Scored(ArticleMatch Article, EntityFacts? Facts, MatchVerdict Verdict);
}

