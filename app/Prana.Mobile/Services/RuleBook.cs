using Prana.Core.Json;
using Prana.Core.Rules;
using Prana.Data;

namespace Prana.Mobile.Services;

/// <summary>
/// The indicator rules that shipped with this build of the app.
/// </summary>
/// <remarks>
/// Loaded once from the packaged rule files. A rule file that fails to parse is skipped rather
/// than crashing the app, but the failure is recorded: an indicator silently disappearing is
/// confusing, and knowing which rule set is missing is the difference between a five-minute
/// diagnosis and an afternoon.
/// </remarks>
public sealed class RuleBook : IRuleProvider
{
    private IReadOnlyList<RuleSet> _rules = [];

    public IReadOnlyList<RuleSet> Rules => _rules;

    /// <summary>Rule files that could not be read, for the settings screen.</summary>
    public IReadOnlyList<string> Failures { get; private set; } = [];

    /// <summary>What the app shows as the version of its analysis.</summary>
    public string Summary => _rules.Count == 0
        ? "No rules loaded"
        : string.Join(", ", _rules.OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => $"{r.Id} v{r.Version}"));

    public async Task<IReadOnlyList<RuleSet>> GetRulesAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return _rules;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_rules.Count > 0)
        {
            return;
        }

        var loaded = new List<RuleSet>();
        var failures = new List<string>();

        foreach (var name in RuleFiles)
        {
            try
            {
                await using var stream = await FileSystem.OpenAppPackageFileAsync(name);
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync(cancellationToken);

                loaded.Add(PranaJson.Deserialize<RuleSet>(json));
            }
            catch (Exception ex) when (ex is FileNotFoundException or System.Text.Json.JsonException)
            {
                failures.Add($"{name}: {ex.Message}");
            }
        }

        _rules = loaded;
        Failures = failures;
    }

    /// <summary>
    /// The packaged rule files, named explicitly.
    /// </summary>
    /// <remarks>
    /// The packaging API can open a named asset but cannot list a directory, so the names are
    /// written out. A rule file added to rules/ and not added here would ship in the package and
    /// never be used, which is why the test suite asserts that this list matches the directory.
    /// </remarks>
    private static readonly string[] RuleFiles =
    [
        "rules/fop-bands-food.json",
        "rules/fop-bands-drink.json",
        "rules/category-peers.json",
    ];

    /// <summary>Exposed so a test can compare it against what is actually in rules/.</summary>
    public static IReadOnlyList<string> PackagedRuleFiles => RuleFiles;
}
