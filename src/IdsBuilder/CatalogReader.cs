using System.Text;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Reads the CSV of programmes written by <c>tools/CatalogDump</c>.
/// </summary>
/// <remarks>
/// The dump quotes every value and separates it with «;», which is the separator Excel expects in
/// Spanish, so the reader is a small state machine that honours the quotes instead of splitting the line:
/// a title such as «Crims; el jurat» would otherwise become two fields.
/// </remarks>
internal static class CatalogReader
{
    /// <summary>Columns of the dump the matcher cannot work without.</summary>
    private static readonly string[] Required = ["id", "titol", "kind", "category"];

    /// <summary>
    /// Reads the programmes of the dump.
    /// </summary>
    /// <param name="path">Path of the CSV.</param>
    /// <param name="includeWeb">Whether the Web and radio programmes are read too.</param>
    /// <returns>The programmes, in the order of the file.</returns>
    /// <exception cref="InvalidDataException">The file is not a dump of the catalog.</exception>
    internal static List<ProgramEntry> Read(string path, bool includeWeb = false)
    {
        var rows = Parse(File.ReadAllText(path, Encoding.UTF8));
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"«{path}» está vacío.");
        }

        var header = rows[0];
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < header.Length; index++)
        {
            columns[header[index].Trim()] = index;
        }

        if (!columns.ContainsKey("id") || !columns.ContainsKey("titol"))
        {
            throw new InvalidDataException(
                $"«{path}» no parece un volcado de 3Cat: faltan las columnas «id» o «titol».");
        }

        foreach (var column in Required)
        {
            if (!columns.ContainsKey(column))
            {
                throw new InvalidDataException($"«{path}» no tiene la columna «{column}».");
            }
        }

        var programs = new List<ProgramEntry>(rows.Count);

        foreach (var row in rows.Skip(1))
        {
            var id = Field(row, columns, "id");
            var title = Field(row, columns, "titol");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var television = string.Equals(Field(row, columns, "kind"), "TV", StringComparison.OrdinalIgnoreCase);
            if (!television && !includeWeb)
            {
                continue;
            }

            programs.Add(new ProgramEntry
            {
                Id = id,
                Title = title,
                DetailTitle = Field(row, columns, "detail_title"),
                OriginalTitle = Field(row, columns, "detail_original_title"),
                Category = Field(row, columns, "category"),
                Countries = Field(row, columns, "detail_countries"),
                AvailableSince = Field(row, columns, "primera_emissio"),
                Url = Field(row, columns, "url"),
                IsTelevision = television
            });
        }

        return programs;
    }

    /// <summary>
    /// Splits a CSV document into rows of fields.
    /// </summary>
    /// <param name="content">Content of the document.</param>
    /// <returns>The rows, the header included.</returns>
    internal static List<string[]> Parse(string content)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var start = content.Length > 0 && content[0] == '\uFEFF' ? 1 : 0;

        for (var index = start; index < content.Length; index++)
        {
            var character = content[index];

            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ';':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. row]);
                    row.Clear();
                    break;
                case '\r':
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }

        return rows;
    }

    private static string? Field(string[] row, Dictionary<string, int> columns, string name)
    {
        if (!columns.TryGetValue(name, out var index) || index >= row.Length)
        {
            return null;
        }

        var value = row[index].Trim();
        return value.Length == 0 ? null : value;
    }
}
