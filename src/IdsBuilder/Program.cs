using System.Globalization;
using System.Text;
using OpenTvIds.IdsBuilder.Emitters;

namespace OpenTvIds.IdsBuilder;

/// <summary>
/// Builds <c>data/&lt;emitter&gt;/series.jsonl</c>: the identifiers of every programme of an emitter in
/// Wikidata, IMDb, TMDB and TVDB.
/// </summary>
/// <remarks>
/// <para>
/// The tool does not call the emitter at all: it reads the CSV dump of the emitter, the list of programmes
/// the catalog publishes with the ids and the titles it uses. Matching that list with the catalogs of the
/// works is the only thing it does, so a new emitter only has to produce its dump.
/// </para>
/// <para>
/// Everything it publishes comes from Wikidata, which is CC0 and can be redistributed; every record keeps
/// the article and the item it comes from, so a consumer can check a record and a person can correct it in
/// <c>overrides/&lt;emitter&gt;.json</c>.
/// </para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // A redirected console has no encoding to change.
        }

        Options options;
        try
        {
            options = Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Usa --help para ver las opciones.");

            return 1;
        }

        if (options.Help)
        {
            PrintHelp();

            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
            Console.Error.WriteLine("Cancelando…");
        };

        try
        {
            if (options.Check)
            {
                return await CheckAsync(options, cancellation.Token).ConfigureAwait(false);
            }

            var overrides = Overrides.Read(options.OverridesPath, Warn);
            using var wikidata = new WikidataClient(options.UserAgent, options.Delay, !options.Offline, Warn);
            var cache = new Cache(options.CacheDir, !options.Fresh, Warn);
            var catalog = new CsvCatalog(options.InputPath, options.Emitter);
            var builder = new Builder(options, catalog, cache, wikidata, overrides, Console.WriteLine, options.Verbose ? Warn : null);
            var result = await builder.RunAsync(cancellation.Token).ConfigureAwait(false);

            DatasetWriter.WriteDataset(options.DatasetPath, result.Records);
            DatasetWriter.WriteReview(options.ReviewPath, options.Emitter, result);
            CoverageReport.Write(options.ReportPath, result, options, DateOnly.FromDateTime(DateTime.UtcNow));

            Summary(result, options);

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelado: los archivos escritos hasta ahora están completos.");

            return 1;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(ex.Message);

            return 1;
        }
    }


    /// <summary>
    /// Prints the summary of a finished build.
    /// </summary>
    /// <param name="result">The result of the build.</param>
    /// <param name="options">Options of the build.</param>
    private static void Summary(BuildResult result, Options options)
    {
        var total = result.Outcomes.Count;
        var published = result.Records.Count;
        var review = result.Outcomes.Count(outcome => outcome.Manual is null && outcome.Verdict.State == MatchState.Review);

        Console.WriteLine($"Hecho en {(int)result.Elapsed.TotalMinutes}:{result.Elapsed.Seconds:00}.");
        Console.WriteLine($"   {published} de {total} programas publicados ({Percent(published, total)}), "
            + $"{review} en revisión, {total - published - review} sin coincidencia.");
        Console.WriteLine($"   {Path.GetFullPath(options.DatasetPath)}");
        Console.WriteLine($"   {Path.GetFullPath(options.ReviewPath)}");
        Console.WriteLine($"   {Path.GetFullPath(options.ReportPath)}");

        if (options.Offline)
        {
            Console.WriteLine("   (sin red: solo se ha usado la caché, los huecos son de ejecuciones anteriores)");
        }
    }

    private static string Percent(int value, int total)
        => total == 0
            ? "—"
            : (100.0 * value / total).ToString("0.0", CultureInfo.InvariantCulture) + " %";

    /// <summary>
    /// Reviews the programmes of the review file with a person, which is the other half of a build: the tool
    /// matches what it can and the person decides what is left.
    /// </summary>
    /// <param name="options">Options, with the emitter and the root of the repository.</param>
    /// <param name="cancellationToken">Token to stop the review.</param>
    /// <returns>0 when the review ended, 1 when there is no review file.</returns>
    private static async Task<int> CheckAsync(Options options, CancellationToken cancellationToken)
    {
        if (!File.Exists(options.ReviewPath))
        {
            Console.Error.WriteLine(
                $"No existe «{options.ReviewPath}»: genera el informe con un volcado normal y vuelve a --check.");

            return 1;
        }

        using var wikidata = new WikidataClient(options.UserAgent, options.Delay, !options.Offline, Warn);

        Func<string, CancellationToken, Task<EntityFacts?>>? resolve = options.Offline
            ? null
            : async (qid, token) =>
            {
                var items = await wikidata.GetFactsAsync([qid], token).ConfigureAwait(false);

                return items.GetValueOrDefault(qid);
            };

        var session = new ReviewSession(options, Console.In, Console.Out, resolve);
        var rows = RevisionSheet.Load(options.ReviewPath, options.Emitter);

        return await session.RunAsync(rows, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the command line.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">An option or its value is not valid.</exception>
    private static Options Parse(string[] args)
    {
        var options = new Options();

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--emitter":
                    options.Emitter = Value(args, ref index);
                    break;
                case "-i":
                case "--input":
                    options.InputPath = Value(args, ref index);
                    break;
                case "-o":
                case "--output":
                    options.OutputDir = Value(args, ref index);
                    break;
                case "--limit":
                    options.Limit = Number(Value(args, ref index), 0);
                    break;
                case "--skip":
                    options.Skip = Number(Value(args, ref index), 0);
                    break;
                case "--include-web":
                    options.IncludeWeb = true;
                    break;
                case "--accept":
                    options.Accept = Confidence(Value(args, ref index));
                    break;
                case "--review":
                    options.Review = Confidence(Value(args, ref index));
                    break;
                case "--concurrency":
                    options.Concurrency = Math.Clamp(Number(Value(args, ref index), 1), 1, 8);
                    break;
                case "--delay":
                    options.Delay = Number(Value(args, ref index), 0);
                    break;
                case "--cache":
                    options.CacheDir = Value(args, ref index);
                    break;
                case "--user-agent":
                    options.UserAgent = Value(args, ref index);
                    break;
                case "--fresh":
                    options.Fresh = true;
                    break;
                case "--offline":
                    options.Offline = true;
                    break;
                case "--check":
                    options.Check = true;
                    break;
                case "--all":
                    options.CheckAll = true;
                    break;
                case "--again":
                    options.CheckAgain = true;
                    break;
                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                case "-h":
                case "--help":
                    options.Help = true;
                    break;
                default:
                    throw new ArgumentException($"Opción desconocida: {args[index]}.");
            }
        }

        if (!options.Help)
        {
            if (options.Review > options.Accept)
            {
                throw new ArgumentException("--review no puede ser mayor que --accept.");
            }

            if (!options.Check)
            {
                options.InputPath = FindInput(options.InputPath);
            }
        }

        return options;
    }


    /// <summary>
    /// Finds the dump of the catalog, which is usually in the root of the repository even when the tool is
    /// run from its own folder.
    /// </summary>
    /// <param name="path">Path given in the command line.</param>
    /// <returns>The path of the file.</returns>
    /// <exception cref="InvalidDataException">The dump does not exist.</exception>
    private static string FindInput(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        for (var level = 0; level < 5 && directory is not null; level++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, path);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidDataException(
            $"No se encuentra «{path}». Copia el volcado del emisor en «input/» "
            + "o indica su ruta con -i <csv>.");
    }

    /// <summary>
    /// Writes a warning.
    /// </summary>
    /// <param name="message">The warning.</param>
    private static void Warn(string message) => Console.Error.WriteLine($"aviso: {message}");

    /// <summary>
    /// Writes the help.
    /// </summary>
    private static void PrintHelp()
        => Console.WriteLine(
            """
            open-tv-ids: identificadores externos (Wikidata, IMDb, TMDB, TVDB) de un catálogo.

            Uso:
              dotnet run --project src/IdsBuilder -- [opciones]

            Opciones:
                  --emitter <slug>    Catálogo de los programas (por defecto, 3cat).
              -i, --input <csv>       Volcado del emisor (por defecto, input/<emisor>.csv).
              -o, --output <dir>      Raíz del repositorio (por defecto, el actual).
                  --limit <n>         Procesa como mucho n programas (0 = todos, por defecto).
                  --skip <n>          Se salta los n primeros programas del volcado.
                  --include-web       Incluye los programas de web y radio (por defecto, solo TV).
                  --accept <x>        Confianza mínima para publicar (por defecto, 0,90).
                  --review <x>        Confianza mínima para revisar (por defecto, 0,70).
                  --concurrency <n>   Programas a la vez (1-8, por defecto 4).
                  --delay <ms>        Pausa entre peticiones (por defecto 60).
                  --cache <dir>       Carpeta de la caché (por defecto, la temporal del usuario).
                  --user-agent <ua>   User-Agent de las peticiones a Wikipedia y Wikidata.
                  --fresh             Ignora la caché y vuelve a preguntar.
                  --offline           No usa la red: solo la caché (para regenerar los informes).
                  --check             Revisa a mano report/<emisor>/revision.csv, programa a programa.
                  --all               Con --check, incluye también los programas sin coincidencia.
                  --again             Con --check, vuelve a los programas que ya tienen corrección.
              -v, --verbose           Muestra los avisos de los endpoints.
              -h, --help              Muestra esta ayuda.

            Ficheros:
              data/<emisor>/series.jsonl    Una línea por programa publicado (ver schema/series.schema.json).
              report/<emisor>/revision.csv  Todos los programas con el estado y la evidencia.
              report/<emisor>/cobertura.md   Cobertura por categoría y qué queda por revisar.
              overrides/<emisor>.json       Correcciones manuales, que mandan sobre el matcher.

            Revisión:
              --check recorre el informe pregunta a pregunta y enseña, de cada programa, la ficha del emisor
              y el elemento de Wikidata para comprobarlo de un vistazo (s = sí, v = sí y verificado, n = no,
              o = otra ficha, d = después, q = salir). Cada respuesta se guarda al momento en
              overrides/<emisor>.json, así que la revisión se puede dejar a medias; después, un volcado con
              --offline la aplica al dataset.

            Detalles:
              El título del catálogo (y el original, si lo publica) se busca en las Wikipedias catalana,
              española e inglesa; el artículo devuelve el elemento de Wikidata y de ahí salen IMDb (P345),
              TMDB (P4983/P4947) y TVDB (P4835). El elemento tiene que ser una serie de televisión o un
              subtipo suyo para sumar la evidencia del tipo, y solo se publica desde 0,90.
              La caché vive en la carpeta temporal del usuario (cambiable con --cache) y no se versiona;
              --offline permite rehacer los informes sin red a partir de lo ya descargado.
            """);

    /// <summary>
    /// Reads the value of an option.
    /// </summary>
    /// <param name="args">Arguments.</param>
    /// <param name="index">Position of the option; it is moved to its value.</param>
    /// <returns>The value.</returns>
    private static string Value(string[] args, ref int index)
    {
        index++;

        return index < args.Length
            ? args[index]
            : throw new ArgumentException("Falta el valor de la última opción.");
    }

    /// <summary>
    /// Reads a number of the command line.
    /// </summary>
    /// <param name="value">Text of the number.</param>
    /// <param name="minimum">Smallest accepted value.</param>
    /// <returns>The number.</returns>
    private static int Number(string value, int minimum)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= minimum
            ? number
            : throw new ArgumentException($"«{value}» no es un número mayor o igual que {minimum}.");

    /// <summary>
    /// Reads a confidence of the command line, which is written with a dot or a comma.
    /// </summary>
    /// <param name="value">Text of the confidence.</param>
    /// <returns>The confidence.</returns>
    private static double Confidence(string value)
        => double.TryParse(
                value.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var confidence)
            && confidence is >= 0 and <= 1
                ? confidence
                : throw new ArgumentException($"«{value}» no es una confianza entre 0 y 1.");
}
