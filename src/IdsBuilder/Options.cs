namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Options of the command line.
/// </summary>
internal sealed class Options
{
    /// <summary>User agent sent to Wikipedia and Wikidata, which identifies the tool and a contact.</summary>
    internal const string DefaultUserAgent =
        "OpenTvIds/0.1 (https://github.com/Lowitle/open-tv-ids; dataset builder)";

    private string? _inputPath;

    /// <summary>
    /// Gets or sets the emitter of the programmes: the catalog the records come from (<c>3cat</c>,
    /// <c>rtve</c>).
    /// </summary>
    /// <remarks>
    /// The emitter is the slug of the files: <c>data/3cat/series.jsonl</c>, <c>report/3cat/cobertura.md</c>,
    /// <c>overrides/3cat.json</c>. One build resolves one emitter.
    /// </remarks>
    public string Emitter { get; set; } = "3cat";

    /// <summary>
    /// Gets or sets the CSV of programmes of the emitter; by default, <c>input/&lt;emitter&gt;.csv</c>.
    /// </summary>
    public string InputPath
    {
        get => _inputPath ?? Path.Combine("input", Emitter + ".csv");
        set => _inputPath = value;
    }

    /// <summary>Gets or sets the root of the repository, where <c>data/</c> and <c>report/</c> live.</summary>
    public string OutputDir { get; set; } = ".";

    /// <summary>Gets or sets how many programmes are skipped before processing; 0 means none.</summary>
    public int Skip { get; set; }

    /// <summary>Gets or sets how many programmes are processed; 0 means all of them.</summary>
    public int Limit { get; set; }

    /// <summary>Gets or sets a value indicating whether the Web and radio programmes are matched too.</summary>
    public bool IncludeWeb { get; set; }

    /// <summary>Gets or sets the confidence from which a record is published.</summary>
    public double Accept { get; set; } = MatchScorer.DefaultAccept;

    /// <summary>Gets or sets the confidence from which a record is worth a manual check.</summary>
    public double Review { get; set; } = MatchScorer.DefaultReview;

    /// <summary>Gets or sets how many programmes are resolved at the same time.</summary>
    /// <remarks>
    /// Four requests at a time is fast enough for the whole catalog and still a small, steady load for the
    /// search endpoint of a Wikipedia, which answers in about a second.
    /// </remarks>
    public int Concurrency { get; set; } = 4;

    /// <summary>Gets or sets the pause between two requests, in milliseconds.</summary>
    public int Delay { get; set; } = 60;

    /// <summary>Gets or sets a value indicating whether the cache is ignored and rewritten.</summary>
    public bool Fresh { get; set; }

    /// <summary>Gets or sets a value indicating whether the network is off and only the cache is used.</summary>
    public bool Offline { get; set; }

    /// <summary>Gets or sets a value indicating whether the warnings of the client are shown.</summary>
    public bool Verbose { get; set; }

    /// <summary>Gets or sets a value indicating whether the help is shown instead of building.</summary>
    public bool Help { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the review file is opened for a person instead of building the
    /// dataset.
    /// </summary>
    public bool Check { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the review also asks about the programmes without a match.
    /// </summary>
    /// <remarks>
    /// They are more than the ones under review and most of them are not works the catalogs have (a programme
    /// of the house, a live show), so they are the second pass and not the default: the review starts with
    /// the programmes the matcher almost found.
    /// </remarks>
    public bool CheckAll { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the review asks again about the programmes that already have a
    /// correction.
    /// </summary>
    public bool CheckAgain { get; set; }

    /// <summary>Gets or sets the user agent.</summary>
    public string UserAgent { get; set; } = DefaultUserAgent;

    /// <summary>Gets the path of the dataset.</summary>
    public string DatasetPath => Path.Combine(OutputDir, "data", Emitter, "series.jsonl");

    /// <summary>Gets the path of the review file.</summary>
    public string ReviewPath => Path.Combine(OutputDir, "report", Emitter, "revision.csv");

    /// <summary>Gets the path of the coverage report.</summary>
    public string ReportPath => Path.Combine(OutputDir, "report", Emitter, "cobertura.md");

    /// <summary>
    /// Gets the path of the catalog for Mix'n'match, which is the file a curator uploads to the import tool of
    /// the community.
    /// </summary>
    /// <remarks>
    /// It is not under <c>report/</c> because it is not a report of the build but the sheet another tool
    /// imports, and <c>refresh.yaml</c> publishes it next to the dataset.
    /// </remarks>
    public string MixNMatchPath => Path.Combine(OutputDir, "mixnmatch", Emitter + ".tsv");

    /// <summary>Gets the path of the manual overrides of the emitter.</summary>
    public string OverridesPath => Path.Combine(OutputDir, "overrides", Emitter + ".json");

    /// <summary>
    /// Gets or sets the folder of the cache.
    /// </summary>
    /// <remarks>
    /// It defaults to the temporary folder of the user instead of the working copy because the cache is
    /// thousands of small files and a working copy on a network drive spends more time listing them than
    /// asking Wikipedia (forty seconds for three thousand files, measured on a NAS).
    /// </remarks>
    public string CacheDir { get; set; } = Path.Combine(Path.GetTempPath(), "open-tv-ids");
}
