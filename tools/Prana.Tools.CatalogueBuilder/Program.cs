using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prana.Tools.CatalogueBuilder;

// Exit codes match the other tools so a workflow can treat them alike.
//   0  built
//   1  built, but the reproducibility check failed
//   2  could not run
const int ExitOk = 0;
const int ExitNotReproducible = 1;
const int ExitUsage = 2;

try
{
    return await RunAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"prana-catalogue: {ex.Message}");
    return ExitUsage;
}

async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] is "--help" or "-h" or "help")
    {
        PrintUsage();
        return arguments.Length == 0 ? ExitUsage : ExitOk;
    }

    if (arguments[0] != "build")
    {
        Console.Error.WriteLine($"prana-catalogue: unknown command '{arguments[0]}'.");
        PrintUsage();
        return ExitUsage;
    }

    string? root = null;
    var output = "catalogue/build";
    var version = 1;
    var starterSize = 2000;
    var minimumApp = "1.0.0";
    var verify = false;
    DateOnly? builtOn = null;

    var rest = arguments[1..];

    for (var i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--root" when i + 1 < rest.Length:
                root = rest[++i];
                break;

            case "--output" when i + 1 < rest.Length:
                output = rest[++i];
                break;

            case "--version" when i + 1 < rest.Length:
                if (!int.TryParse(rest[++i], out version) || version < 1)
                {
                    Console.Error.WriteLine("prana-catalogue: --version must be a positive number.");
                    return ExitUsage;
                }

                break;

            case "--starter-size" when i + 1 < rest.Length:
                if (!int.TryParse(rest[++i], out starterSize) || starterSize < 0)
                {
                    Console.Error.WriteLine("prana-catalogue: --starter-size must be zero or more.");
                    return ExitUsage;
                }

                break;

            case "--minimum-app" when i + 1 < rest.Length:
                minimumApp = rest[++i];
                break;

            case "--built-on" when i + 1 < rest.Length:
                if (!DateOnly.TryParse(rest[++i], CultureInfo.InvariantCulture, out var parsed))
                {
                    Console.Error.WriteLine("prana-catalogue: --built-on must be YYYY-MM-DD.");
                    return ExitUsage;
                }

                builtOn = parsed;
                break;

            case "--verify-reproducible":
                verify = true;
                break;

            default:
                Console.Error.WriteLine($"prana-catalogue: unexpected argument '{rest[i]}'.");
                return ExitUsage;
        }
    }

    var repositoryRoot = root is null ? FindRepositoryRoot() : Path.GetFullPath(root);

    if (repositoryRoot is null)
    {
        Console.Error.WriteLine("prana-catalogue: could not find the repository root. Pass --root.");
        return ExitUsage;
    }

    var outputDirectory = Path.IsPathRooted(output) ? output : Path.Combine(repositoryRoot, output);

    // The build date is stamped into the catalogue, so leaving it to the clock would make two
    // builds of the same data differ across midnight.
    var stamp = builtOn ?? DateOnly.FromDateTime(DateTime.UtcNow);

    BuildOptions For(int starter) => new()
    {
        RepositoryRoot = repositoryRoot,
        OutputDirectory = outputDirectory,
        CatalogueVersion = version,
        BuiltOn = stamp,
        StarterSize = starter,
        MinimumAppVersion = minimumApp,
    };

    Console.WriteLine($"Building catalogue version {version} from {repositoryRoot}");
    Console.WriteLine();

    Console.WriteLine("Full catalogue");
    var full = await new CatalogueBuild(For(0)).ExecuteAsync(Console.Out, CancellationToken.None);
    Report(full);

    BuildResult? starterResult = null;

    if (starterSize > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Starter catalogue");
        starterResult = await new CatalogueBuild(For(starterSize)).ExecuteAsync(Console.Out, CancellationToken.None);
        Report(starterResult);
    }

    await WriteManifestAsync(outputDirectory, version, stamp, minimumApp, full, starterResult);

    if (!verify)
    {
        return ExitOk;
    }

    Console.WriteLine();
    Console.WriteLine("Reproducibility check: building the full catalogue a second time");

    var checkDirectory = Path.Combine(outputDirectory, "verify");

    var second = await new CatalogueBuild(new BuildOptions
    {
        RepositoryRoot = repositoryRoot,
        OutputDirectory = checkDirectory,
        CatalogueVersion = version,
        BuiltOn = stamp,
        StarterSize = 0,
        MinimumAppVersion = minimumApp,
    }).ExecuteAsync(Console.Out, CancellationToken.None);

    Directory.Delete(checkDirectory, recursive: true);

    if (string.Equals(full.Sha256, second.Sha256, StringComparison.Ordinal))
    {
        Console.WriteLine($"  identical: {full.Sha256}");
        return ExitOk;
    }

    Console.Error.WriteLine("  NOT reproducible. The same input produced two different files.");
    Console.Error.WriteLine($"    first  {full.Sha256}");
    Console.Error.WriteLine($"    second {second.Sha256}");

    return ExitNotReproducible;
}

void Report(BuildResult result)
{
    static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

    var saved = result.DatabaseBytes == 0
        ? 0
        : 100 - (result.CompressedBytes * 100 / result.DatabaseBytes);

    Console.WriteLine(
        $"  {result.Products:N0} products ({result.Incomplete:N0} with a name only), "
        + $"{Mb(result.DatabaseBytes)} -> {Mb(result.CompressedBytes)} compressed ({saved}% smaller), "
        + $"{result.Elapsed.TotalSeconds:0.0}s");
}

async Task WriteManifestAsync(
    string directory,
    int version,
    DateOnly builtOn,
    string minimumApp,
    BuildResult full,
    BuildResult? starter)
{
    var manifest = new
    {
        catalogueVersion = version,
        schemaVersion = CatalogueSchema.Version,
        minimumAppVersion = minimumApp,
        builtOn = builtOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        licence = "ODbL-1.0 (database), DbCL-1.0 (contents)",
        attribution = "Contains data from Open Food Facts (https://world.openfoodfacts.org), "
            + "made available under the Open Database License (ODbL) v1.0.",
        full = Describe(full),
        starter = starter is null ? null : Describe(starter),

        // Filled by the release workflow in F06. Present here so the shape a client parses does
        // not change when signing arrives.
        signature = (string?)null,
    };

    var path = Path.Combine(directory, "manifest.json");

    await File.WriteAllTextAsync(
        path,
        JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = "\n",
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        }) + "\n");

    Console.WriteLine();
    Console.WriteLine($"Manifest written to {path}");

    static object Describe(BuildResult r) => new
    {
        file = Path.GetFileName(r.CompressedPath),
        products = r.Products,
        incomplete = r.Incomplete,
        sizeBytes = r.CompressedBytes,
        uncompressedBytes = r.DatabaseBytes,
        sha256 = r.Sha256,
    };
}

string? FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "Prana.sln")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    return null;
}

void PrintUsage() => Console.WriteLine(
    """
    prana-catalogue - builds the SQLite catalogue the app installs.

    Usage:
      prana-catalogue build [options]

    Options:
      --root <dir>            Repository root. Found automatically when omitted.
      --output <dir>          Where to write. Default catalogue/build.
      --version <n>           Catalogue version number. Default 1.
      --starter-size <n>      Products in the bundled starter catalogue. 0 to skip it.
                              Default 2000.
      --minimum-app <v>       App version required to read this catalogue. Default 1.0.0.
      --built-on <date>       Build date stamped into the catalogue, YYYY-MM-DD.
                              Defaults to today. Pass it to make a build reproducible.
      --verify-reproducible   Build the full catalogue twice and compare hashes.
      -h, --help              Show this message.

    Two catalogues are produced. The full one is downloaded after install. The starter one is
    small enough to bundle in the app so the first launch works before anything is downloaded.

    Exit codes:
      0   built
      1   built, but the same input produced two different files
      2   could not run
    """);
