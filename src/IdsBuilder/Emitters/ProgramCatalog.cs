namespace OpenTvIds.IdsBuilder.Emitters;

/// <summary>
/// Source of the programmes of an emitter: the list the matcher walks.
/// </summary>
/// <remarks>
/// An emitter is a catalog of programmes with its own ids and its own dump (<c>3cat</c>, <c>rtve</c>...).
/// The builder does not know where the programmes come from: it asks the catalog of the emitter for the
/// list and matches it against Wikidata.
/// </remarks>
internal interface IProgramCatalog
{
    /// <summary>Gets the emitter of the programmes (<c>3cat</c>, <c>rtve</c>).</summary>
    string Emitter { get; }

    /// <summary>
    /// Reads the programmes.
    /// </summary>
    /// <param name="includeWeb">Whether the Web and radio programmes are read too.</param>
    /// <returns>The programmes, in the order of the dump.</returns>
    IReadOnlyList<ProgramEntry> Read(bool includeWeb);
}

/// <summary>
/// Reads the programmes of an emitter from its CSV dump.
/// </summary>
/// <remarks>
/// The dump is the only thing an emitter has to produce: one row per programme with <c>id</c>,
/// <c>titol</c>, <c>kind</c> and <c>category</c> and, when it knows them, <c>detail_title</c>,
/// <c>detail_original_title</c>, <c>detail_countries</c> and <c>primera_emissio</c>. The contract is
/// documented in <c>input/README.md</c>.
/// </remarks>
/// <param name="path">Path of the CSV.</param>
/// <param name="emitter">Emitter of the programmes.</param>
internal sealed class CsvCatalog(string path, string emitter) : IProgramCatalog
{
    /// <inheritdoc />
    public string Emitter => emitter;

    /// <inheritdoc />
    public IReadOnlyList<ProgramEntry> Read(bool includeWeb) => CatalogReader.Read(path, includeWeb);
}