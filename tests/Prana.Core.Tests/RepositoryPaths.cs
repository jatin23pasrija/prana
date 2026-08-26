namespace Prana.Core.Tests;

/// <summary>
/// Locates repository directories from wherever the test binary happens to run. Walking up to
/// the solution file keeps the tests working whether they are started by the IDE, by
/// <c>dotnet test</c> from the repository root, or by CI from somewhere else entirely.
/// </summary>
internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    public static string Schemas => Path.Combine(Root, "schema");

    public static string ValidExamples => Path.Combine(Schemas, "examples", "valid");

    public static string InvalidExamples => Path.Combine(Schemas, "examples", "invalid");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Prana.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find Prana.sln above {AppContext.BaseDirectory}.");
    }

    /// <summary>
    /// Which schema a record belongs to, taken from the file name prefix. The examples are
    /// named this way so a new case can be added by dropping in a file, with no test change.
    /// </summary>
    public static string SchemaNameFor(string recordPath)
    {
        var name = Path.GetFileNameWithoutExtension(recordPath);
        var dash = name.IndexOf('-');
        return dash < 0 ? name : name[..dash];
    }
}
