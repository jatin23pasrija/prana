using Prana.Core.Json;
using Prana.Core.Rules;
using Xunit;

namespace Prana.Data.Tests;

/// <summary>
/// Every rule file in rules/ must actually reach the phone.
/// </summary>
/// <remarks>
/// The packaging API can open a named asset but cannot list a directory, so the app names its
/// rule files in two places: the csproj that packages them and the loader that reads them. A rule
/// file added to rules/ and forgotten in either would be validated by CI, reviewed in the pull
/// request, and silently never used, with the only symptom being an indicator that does not
/// appear. That is a bad failure to debug, so it is a test instead.
/// </remarks>
public sealed class RulePackagingTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Prana.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static IReadOnlyList<string> RuleFileNames() =>
        [.. Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "rules"), "*.json", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .OrderBy(n => n, StringComparer.Ordinal)!];

    [Fact]
    public void Every_rule_file_is_packaged_into_the_app()
    {
        var csproj = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "app", "Prana.Mobile", "Prana.Mobile.csproj"));

        foreach (var name in RuleFileNames())
        {
            Assert.True(
                csproj.Contains($"rules/{name}", StringComparison.Ordinal),
                $"rules/{name} is not packaged. Add a MauiAsset entry for it in Prana.Mobile.csproj.");
        }
    }

    [Fact]
    public void Every_rule_file_is_loaded_by_the_app()
    {
        var loader = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "app", "Prana.Mobile", "Services", "RuleBook.cs"));

        foreach (var name in RuleFileNames())
        {
            Assert.True(
                loader.Contains($"rules/{name}", StringComparison.Ordinal),
                $"rules/{name} is packaged but never loaded. Add it to RuleBook.RuleFiles.");
        }
    }

    [Fact]
    public void Every_rule_file_parses_and_cites_a_source()
    {
        // The citation is the whole promise of the format, so it is checked on the real files
        // rather than trusted to the schema alone.
        foreach (var path in Directory.EnumerateFiles(
            Path.Combine(RepositoryRoot(), "rules"), "*.json", SearchOption.AllDirectories))
        {
            var rules = PranaJson.Deserialize<RuleSet>(File.ReadAllText(path));

            Assert.False(string.IsNullOrWhiteSpace(rules.Source.Title));
            Assert.False(string.IsNullOrWhiteSpace(rules.Source.Publisher));
            Assert.False(string.IsNullOrWhiteSpace(rules.Source.Licence));

            // A locator is what lets a reviewer check a transcription without reading the whole
            // document. Its absence is how a wrong threshold survives review.
            Assert.False(string.IsNullOrWhiteSpace(rules.Source.Locator));
            Assert.StartsWith("https://", rules.Source.Url);
        }
    }

    [Fact]
    public void Rule_identifiers_are_unique()
    {
        var ids = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "rules"), "*.json", SearchOption.AllDirectories)
            .Select(f => PranaJson.Deserialize<RuleSet>(File.ReadAllText(f)).Id)
            .ToList();

        // Indicators are attributed by id, so two rule sets sharing one would make an
        // explanation point at the wrong document.
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
