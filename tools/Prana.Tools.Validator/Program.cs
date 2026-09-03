using Prana.Tools.Validator;

// Exit codes are part of the contract with CI and with later workflows, so they are documented
// in docs/VALIDATION.md and must not change meaning.
//   0  nothing blocking
//   1  blocking findings, meaning errors, or warnings under --strict
//   2  the tool could not run: bad arguments, missing schemas, missing paths
const int ExitOk = 0;
const int ExitFindings = 1;
const int ExitUsage = 2;

try
{
    return Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"prana-validate: {ex.Message}");
    return ExitUsage;
}

int Run(string[] arguments)
{
    if (arguments.Length == 0 || arguments[0] is "--help" or "-h" or "help")
    {
        PrintUsage();
        return arguments.Length == 0 ? ExitUsage : ExitOk;
    }

    var command = arguments[0];
    var rest = arguments[1..];

    return command switch
    {
        "validate" => Validate(rest),
        "format" => Format(rest),
        _ => Unknown(command),
    };
}

int Unknown(string command)
{
    Console.Error.WriteLine($"prana-validate: unknown command '{command}'.");
    PrintUsage();
    return ExitUsage;
}

int Validate(string[] arguments)
{
    var strict = false;
    var format = ReportFormat.Human;
    string? root = null;
    string? schema = null;
    var paths = new List<string>();

    for (var i = 0; i < arguments.Length; i++)
    {
        switch (arguments[i])
        {
            case "--strict":
                strict = true;
                break;

            case "--format" when i + 1 < arguments.Length:
                if (!TryParseFormat(arguments[++i], out format))
                {
                    Console.Error.WriteLine($"prana-validate: unknown format '{arguments[i]}'.");
                    return ExitUsage;
                }

                break;

            case "--root" when i + 1 < arguments.Length:
                root = arguments[++i];
                break;

            case "--schema" when i + 1 < arguments.Length:
                schema = arguments[++i];
                break;

            default:
                paths.Add(arguments[i]);
                break;
        }
    }

    var repositoryRoot = root is null ? FindRepositoryRoot() : Path.GetFullPath(root);

    if (repositoryRoot is null)
    {
        Console.Error.WriteLine("prana-validate: could not find the repository root. Pass --root.");
        return ExitUsage;
    }

    if (paths.Count == 0)
    {
        paths.Add(Path.Combine(repositoryRoot, "data"));
        paths.Add(Path.Combine(repositoryRoot, "rules"));
    }

    var missing = paths.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();

    if (missing.Count > 0)
    {
        foreach (var path in missing)
        {
            Console.Error.WriteLine($"prana-validate: no such file or directory: {path}");
        }

        return ExitUsage;
    }

    var run = new ValidationRun(new ValidationOptions
    {
        RepositoryRoot = repositoryRoot,
        SchemaDirectory = schema is null
            ? Path.Combine(repositoryRoot, "schema")
            : Path.GetFullPath(schema),
        Paths = paths,
        Strict = strict,
    });

    var result = run.Execute();

    Reporting.Write(Console.Out, format, result.Diagnostics, result.FilesChecked, result.Elapsed, strict);

    return result.Blocks(strict) ? ExitFindings : ExitOk;
}

int Format(string[] arguments)
{
    var checkOnly = false;
    var paths = new List<string>();

    foreach (var argument in arguments)
    {
        if (argument == "--check")
        {
            checkOnly = true;
        }
        else
        {
            paths.Add(argument);
        }
    }

    if (paths.Count == 0)
    {
        var root = FindRepositoryRoot();

        if (root is null)
        {
            Console.Error.WriteLine("prana-validate: could not find the repository root.");
            return ExitUsage;
        }

        paths.Add(Path.Combine(root, "data"));
        paths.Add(Path.Combine(root, "rules"));
    }

    var changed = new List<string>();
    var failed = false;

    foreach (var file in ValidationRun.EnumerateFiles(paths))
    {
        string text;

        try
        {
            text = File.ReadAllText(file);
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"{file}: {ex.Message}");
            failed = true;
            continue;
        }

        try
        {
            if (CanonicalFormat.IsCanonical(text, out var canonical))
            {
                continue;
            }

            changed.Add(file);

            if (!checkOnly)
            {
                File.WriteAllText(file, canonical);
            }
        }
        catch (System.Text.Json.JsonException ex)
        {
            // Formatting cannot fix a file that is not JSON, and pretending otherwise would
            // either destroy content or hide the real problem.
            Console.Error.WriteLine($"{file}: not valid JSON, so it cannot be formatted. {ex.Message}");
            failed = true;
        }
    }

    if (failed)
    {
        return ExitUsage;
    }

    if (changed.Count == 0)
    {
        Console.WriteLine("All files are already in the canonical format.");
        return ExitOk;
    }

    foreach (var file in changed)
    {
        Console.WriteLine(checkOnly ? $"would reformat: {file}" : $"formatted: {file}");
    }

    Console.WriteLine(checkOnly
        ? $"{changed.Count} file(s) are not in the canonical format. Run the same command without --check to fix them."
        : $"Reformatted {changed.Count} file(s).");

    return checkOnly ? ExitFindings : ExitOk;
}

bool TryParseFormat(string value, out ReportFormat format)
{
    switch (value.ToLowerInvariant())
    {
        case "human":
            format = ReportFormat.Human;
            return true;

        case "github":
            format = ReportFormat.GitHub;
            return true;

        case "json":
            format = ReportFormat.Json;
            return true;

        default:
            format = ReportFormat.Human;
            return false;
    }
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
    prana-validate - checks Prana data records.

    Usage:
      prana-validate validate [paths...] [options]
      prana-validate format   [paths...] [--check]

    Commands:
      validate   Check records against the schema and the data rules.
      format     Rewrite records into the canonical format.

    Options:
      --strict            Treat warnings as errors. Used by the import quality gate.
      --format <f>        human (default), github, or json.
      --root <dir>        Repository root. Found automatically when omitted.
      --schema <dir>      Schema directory. Defaults to <root>/schema.
      --check             format only. Report what would change and exit 1, changing nothing.
      -h, --help          Show this message.

    With no paths given, both commands act on data/.

    Exit codes:
      0   nothing blocking
      1   blocking findings, or files needing formatting under --check
      2   the tool could not run

    Rule codes are documented in docs/VALIDATION.md.
    """);
