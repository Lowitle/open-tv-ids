using System.Text;
using System.Text.Json;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// An identifier a person has confirmed by hand.
/// </summary>
internal sealed class ManualOverride
{
    /// <summary>Gets or sets the global id of the record (<c>3cat:60336</c>).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifiers to publish, which replace the ones the matcher found: the override says
    /// everything that is published, not only what changes.
    /// </summary>
    /// <remarks>
    /// Replacing instead of merging is what makes a correction safe. When the matcher picks the wrong item
    /// («Polònia», the country, instead of «Polònia», the programme), merging kept the IMDb, TMDB and TVDB
    /// ids of the wrong item as soon as the person only wrote the item of the right one.
    /// </remarks>
    public Dictionary<string, string> Ids { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Gets or sets the note that explains the correction, which is copied to the review file.</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the identifiers were confirmed against a second source
    /// (the TMDB page, the TVDB page), which is what marks the record as verified.
    /// </summary>
    public bool Verified { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the programme must stay out of the dataset because the match
    /// is wrong and no other item is known.
    /// </summary>
    /// <remarks>
    /// Without this the only correction was to point at another item, so a false positive without a
    /// replacement came back in every build and took a second look each time.
    /// </remarks>
    public bool Reject { get; set; }
}

/// <summary>
/// Reads <c>overrides/&lt;emitter&gt;.json</c>, the file where a person corrects or completes a record.
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
        record.Ids.Clear();

        if (manual.Reject)
        {
            record.Match.Method = "manual";
            record.Match.Confidence = 1;
            record.Match.Verified = false;

            return;
        }

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

    /// <summary>
    /// Writes the overrides, sorted by id so a diff of the file only touches what changed.
    /// </summary>
    /// <param name="path">Path of the file.</param>
    /// <param name="overrides">The overrides, keyed by the global id of the record.</param>
    /// <remarks>
    /// The file is written indented, one field per line: it is the one file of the repository a person
    /// edits by hand and reviews in a pull request.
    /// </remarks>
    internal static void Write(string path, IReadOnlyDictionary<string, ManualOverride> overrides)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var list = overrides.Values
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .ToList();

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(list, OverridesJson.Options) + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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

/// <summary>
/// The JSON dialect of <c>overrides/&lt;emitter&gt;.json</c>.
/// </summary>
internal static class OverridesJson
{
    /// <summary>
    /// Gets the options the file is written with: the dialect of the dataset, indented and with line feeds,
    /// so it is readable and does not fight the line endings of the working copy.
    /// </summary>
    internal static JsonSerializerOptions Options { get; } = new(DatasetJson.Options)
    {
        WriteIndented = true,
        NewLine = "\n"
    };
}
