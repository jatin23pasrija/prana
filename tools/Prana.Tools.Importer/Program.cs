using System.Globalization;
using Prana.Tools.Importer;
using Prana.Tools.Importer.Sources.OpenFoodFacts;

// Exit codes match the validator, so a workflow can treat both tools the same way.
//   0  the import ran
//   1  the import ran but produced nothing usable
//   2  the tool could not run
const int ExitOk = 0;
const int ExitEmpty = 1;
const int ExitUsage = 2;

try
{
    return await RunAsync(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"prana-import: {ex.Message}");
    return ExitUsage;
}

async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] is "--help" or "-h" or "help")
    {
        PrintUsage();
        return arguments.Length == 0 ? ExitUsage : ExitOk;
    }

    if (arguments[0] != "import")
    {
        Console.Error.WriteLine($"prana-import: unknown command '{arguments[0]}'.");
        PrintUsage();
        return ExitUsage;
    }

    string? dump = null;
    string? root = null;
    string? reportPath = null;
    string? url = null;
    var source = "openfoodfacts";
    var limit = 0;
    var dryRun = false;
    DateOnly? retrievedAt = null;

    var rest = arguments[1..];

    for (var i = 0; i < rest.Length; i++)
    {
        switch (rest[i])
        {
            case "--source" when i + 1 < rest.Length:
                source = rest[++i];
                break;

            case "--dump" when i + 1 < rest.Length:
                dump = rest[++i];
                break;

            case "--root" when i + 1 < rest.Length:
                root = rest[++i];
                break;

            case "--report" when i + 1 < rest.Length:
                reportPath = rest[++i];
                break;

            case "--url" when i + 1 < rest.Length:
                url = rest[++i];
                break;

            case "--retrieved-at" when i + 1 < rest.Length:
                if (!DateOnly.TryParse(rest[++i], CultureInfo.InvariantCulture, out var parsed))
                {
                    Console.Error.WriteLine("prana-import: --retrieved-at must be YYYY-MM-DD.");
                    return ExitUsage;
                }

                retrievedAt = parsed;
                break;

            case "--limit" when i + 1 < rest.Length:
                if (!int.TryParse(rest[++i], out limit) || limit < 0)
                {
                    Console.Error.WriteLine("prana-import: --limit must be a non-negative number.");
                    return ExitUsage;
                }

                break;

            case "--dry-run":
                dryRun = true;
                break;

            default:
                Console.Error.WriteLine($"prana-import: unexpected argument '{rest[i]}'.");
                return ExitUsage;
        }
    }

    if (source != "openfoodfacts")
    {
        Console.Error.WriteLine(
            $"prana-import: no adapter for '{source}'. Only sources approved in DATA_SOURCES.md can be imported.");

        return ExitUsage;
    }

    if (dump is null)
    {
        Console.Error.WriteLine(
            "prana-import: --dump is required. Give a path, or - to read the export from standard input.");

        return ExitUsage;
    }

    var fromStdin = dump == "-";

    if (!fromStdin && !File.Exists(dump))
    {
        Console.Error.WriteLine($"prana-import: no such file: {dump}");
        return ExitUsage;
    }

    var repositoryRoot = root is null ? FindRepositoryRoot() : Path.GetFullPath(root);

    if (repositoryRoot is null)
    {
        Console.Error.WriteLine("prana-import: could not find the repository root. Pass --root.");
        return ExitUsage;
    }

    // The export carries no date inside it, and the retrieval date is stamped onto every
    // record, so getting it wrong misdates the whole import. A file has a timestamp to fall back
    // on. A stream does not, so it has to be told.
    if (fromStdin && retrievedAt is null)
    {
        Console.Error.WriteLine(
            "prana-import: --retrieved-at is required when reading from standard input, "
            + "because a stream has no timestamp to fall back on.");

        return ExitUsage;
    }

    var retrieved = retrievedAt ?? DateOnly.FromDateTime(File.GetLastWriteTimeUtc(dump));

    var adapter = new OffJsonlAdapter(OffJsonlAdapter.Open(dump), retrieved);

    Console.WriteLine($"Importing {adapter.DisplayName}, retrieved {retrieved:yyyy-MM-dd}.");
    Console.WriteLine($"Licence: {adapter.Licence}");
    Console.WriteLine();

    var run = new ImportRun(new ImportOptions
    {
        RepositoryRoot = repositoryRoot,
        Adapter = adapter,
        Limit = limit,
        DryRun = dryRun,
    });

    var report = await run.ExecuteAsync(Console.Out, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine(report.ToSummary());

    if (reportPath is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
        await File.WriteAllTextAsync(reportPath, report.ToJson() + "\n");
        Console.WriteLine($"{Environment.NewLine}Report written to {reportPath}");
    }

    if (!dryRun && url is not null)
    {
        var snapshotPath = Path.Combine(repositoryRoot, "sources", adapter.Id, "snapshot.json");
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);

        const string note = "JSONL export. Filtered to products sold in India or carrying a GS1 India barcode.";

        // A streamed export was never on disk, so there is nothing to checksum. Recording the
        // url and date without a hash is honest; inventing one would not be.
        var snapshot = fromStdin
            ? Snapshot.Streamed(adapter, url, note)
            : Snapshot.Describe(dump, adapter, url, note);

        await File.WriteAllTextAsync(snapshotPath, snapshot.ToJson());
        Console.WriteLine($"Snapshot written to {snapshotPath}");
    }

    return report.Written == 0 && !dryRun ? ExitEmpty : ExitOk;
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
    prana-import - brings an approved external source into the repository.

    Usage:
      prana-import import --dump <file> [options]
      curl -sL <export-url> | gunzip | prana-import import --dump - --retrieved-at <date>

    The Open Food Facts JSONL export is around 12 GB compressed, so the piped form is the
    normal one: nothing is ever written to disk.

    Options:
      --source <id>        Source to import. Default openfoodfacts.
      --dump <file>        The source export to read, or - for standard input. Required.
      --root <dir>         Repository root. Found automatically when omitted.
      --retrieved-at <d>   Retrieval date, YYYY-MM-DD. Defaults to the dump file timestamp,
                           and is required when reading from standard input.
      --url <url>          Where the dump came from. Writes sources/<id>/snapshot.json.
      --report <file>      Write a JSON report of what was imported and dropped.
      --limit <n>          Stop after n mapped records. For smoke runs.
      --dry-run            Map and report, write nothing.
      -h, --help           Show this message.

    Only sources with an approved row in DATA_SOURCES.md have an adapter. That is deliberate:
    the licence review is the gate, and code should not be able to route around it.

    Exit codes:
      0   the import ran
      1   the import ran but wrote nothing
      2   the tool could not run
    """);
