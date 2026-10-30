using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Keeps the answers of the Wikipedias and of Wikidata between runs, so a second run of the builder costs
/// nothing and a failed run does not start over.
/// </summary>
/// <remarks>
/// The cache is a development aid, not the dataset: it lives under <c>dataset/.cache</c>, which is ignored
/// by git, and it can be skipped with <c>--fresh</c>. Every file is keyed by the request (the language and
/// the title of the search, the item of the query), so a change of the catalog only invalidates the
/// programmes whose title changed.
/// </remarks>
internal sealed class Cache
{
    private readonly string _root;
    private readonly bool _read;
    private readonly Action<string>? _log;

    /// <summary>
    /// Keys of the entries already on disk, per area.
    /// </summary>
    /// <remarks>
    /// The cache lives under the working copy, which on this project is a network drive: a <c>File.Exists</c>
    /// per request costs tens of milliseconds there, and a whole build asks thousands of times. Listing each
    /// area once and asking the list afterwards keeps the same behaviour with one directory read.
    /// </remarks>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _known =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="Cache"/> class.
    /// </summary>
    /// <param name="root">Folder of the cache.</param>
    /// <param name="read">
    /// Whether the cache is read. When it is not (<c>--fresh</c>), the answers are still written, so the next
    /// build starts from what this one learned.
    /// </param>
    /// <param name="log">Sink of the warnings, or <c>null</c>.</param>
    internal Cache(string root, bool read, Action<string>? log = null)
    {
        _root = root;
        _read = read;
        _log = log;
    }

    /// <summary>
    /// Reads an entry of the cache.
    /// </summary>
    /// <typeparam name="T">Type of the entry.</typeparam>
    /// <param name="area">Group of the entry (<c>search</c>, <c>entities</c>).</param>
    /// <param name="key">Request the entry answers.</param>
    /// <param name="value">The entry, when it is cached.</param>
    /// <returns>Whether the entry was cached.</returns>
    internal bool TryGet<T>(string area, string key, out T? value)
        where T : class
    {
        value = null;
        if (!_read)
        {
            return false;
        }

        var path = PathOf(area, key);
        var hash = Path.GetFileNameWithoutExtension(path);
        if (!Known(area).ContainsKey(hash) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path, Encoding.UTF8), DatasetJson.Options);

            return value is not null;
        }
        catch (JsonException ex)
        {
            // A half-written file (the run was interrupted) is not worth a failure: it is dropped and the
            // next run asks again.
            _log?.Invoke($"Caché ilegible en {path}: {ex.Message}");
            File.Delete(path);
            Known(area).TryRemove(hash, out _);

            return false;
        }
    }

    /// <summary>
    /// Writes an entry of the cache.
    /// </summary>
    /// <typeparam name="T">Type of the entry.</typeparam>
    /// <param name="area">Group of the entry (<c>search</c>, <c>entities</c>).</param>
    /// <param name="key">Request the entry answers.</param>
    /// <param name="value">The entry.</param>
    internal void Set<T>(string area, string key, T value)
    {
        var path = PathOf(area, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, DatasetJson.Options), Encoding.UTF8);
        Known(area).TryAdd(Path.GetFileNameWithoutExtension(path), 0);
    }

    private ConcurrentDictionary<string, byte> Known(string area)
        => _known.GetOrAdd(area, name =>
        {
            var keys = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            var folder = Path.Combine(_root, name);

            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
                {
                    keys.TryAdd(Path.GetFileNameWithoutExtension(file), 0);
                }
            }

            return keys;
        });

    private string PathOf(string area, string key)
        => Path.Combine(_root, area, Hash(key) + ".json");

    private static string Hash(string key)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
}
