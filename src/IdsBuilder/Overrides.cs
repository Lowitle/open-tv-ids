using System.Text.Json;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// An identifier a person has confirmed by hand.
/// </summary>
internal sealed class ManualOverride
{
    /// <summary>Gets or sets the global id of the record (<c>3cat:60336</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the identifiers to publish.</summary>
    public Dictionary<string, string> Ids { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the note that explains the correction, which is copied to the review file.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the identifiers were confirmed against a second source
    /// (the TMDB page, the TVDB page), which is what marks the record as verified.
    /// </summary>
    public bool Verified { get; set; }
}

/// <summary>
/// Reads <c>dataset/overrides/manual.json</c>, the file where a person corrects or completes a record.
/// </summary>
/// <remarks>
/// The matcher is a heuristic: it will always leave programmes without identifiers (a series whose
/// Wikipedia article does not exist) and it will sometimes pick the wrong item. The overrides are the way
/// to fix both without touching the code, and they survive every rebuild of the dataset because the file
/// is not generated.
/// </remarks>
internal static class Overrides
{
    /// <summary>
    /// Reads the overrides.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="log">Sink of the warnings, or <c>null</c>.</param>
    /// <returns>The overrides, keyed by the global id of the record; empty when the file does not exist.</returns>
    internal static Dictionary<string, ManualOverride> Read(string path, Action<string>? log = null)
    {
        var overrides = new Dictionary<string, ManualOverride>(StringComparer.Ordinal);

        if (!File.Exists(path))
        {
            return overrides;
        }

        List<ManualOverride>? list;
        try
        {
            list = JsonSerializer.Deserialize<List<ManualOverride>>(File.ReadAllText(path), DatasetJson.Options);
        }
        catch (JsonException ex)
        {
            log?.Invoke($"No se pudo leer {path}: {ex.Message}");
            return overrides;
        }

        foreach (var item in list ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                continue;
            }

            if (!overrides.TryAdd(item.Id, item))
            {
                log?.Invoke($"El id «{item.Id}» aparece más de una vez en {path}.");
            }
        }

        return overrides;
    }

    /// <summary>
    /// Applies an override to a record: the identifiers replace the ones the matcher found, and the record
    /// is published as verified when the override says so.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="manual">The override.</param>
    internal static void Apply(SeriesIdRecord record, ManualOverride manual)
    {
        foreach (var (catalog, value) in manual.Ids)
        {
            var identifier = Check(catalog, value);
            if (identifier is not null)
            {
                record.Ids[catalog] = identifier;
            }
        }

        record.Match.Method = "manual";
        record.Match.Confidence = 1;
        record.Match.Verified = manual.Verified;

        if (!record.Sources.Contains("manual", StringComparer.Ordinal))
        {
            record.Sources.Add("manual");
        }
    }

    private static string? Check(string catalog, string? value)
        => catalog switch
        {
            "wikidata" => ExternalIds.Wikidata(value),
            "imdb" => ExternalIds.Imdb(value),
            "tmdb" => ExternalIds.Tmdb(value),
            "tvdb" => ExternalIds.Tvdb(value),
            _ => string.IsNullOrWhiteSpace(value) ? null : value.Trim()
        };
}
