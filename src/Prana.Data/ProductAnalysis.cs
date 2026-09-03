using Prana.Core.Model;
using Prana.Core.Rules;

namespace Prana.Data;

/// <summary>
/// Supplies the indicator rules that shipped with this build.
/// </summary>
/// <remarks>
/// An interface because the rules are read from the app package, which only exists on a device.
/// Keeping the analysis behind it is what lets the rules that matter most be tested: that an
/// undeclared nutrient renders as unknown rather than zero, and that a record holding only a
/// name is still marked incomplete.
/// </remarks>
public interface IRuleProvider
{
    Task<IReadOnlyList<RuleSet>> GetRulesAsync(CancellationToken cancellationToken);
}

/// <summary>How fresh a record is, from its verification date.</summary>
public enum Freshness
{
    Current,
    ReviewRecommended,
    PossiblyOutdated,
    Unknown,
}

/// <summary>One row of the nutrition table.</summary>
/// <param name="Value">Null when the packet does not declare it. Rendered as Unknown, never 0.</param>
public sealed record NutrientRow(string Nutrient, string Display, double? Value, string Unit)
{
    public string DisplayValue => Value is null
        ? "Unknown"
        : IndicatorEngineFormat(Value.Value) + " " + Unit;

    public bool IsKnown => Value is not null;

    private static string IndicatorEngineFormat(double value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>One nutrition panel, ready to render.</summary>
public sealed record PanelView(
    string BasisLabel,
    string? ServingLabel,
    IReadOnlyList<NutrientRow> Rows,
    IReadOnlyList<Indicator> Indicators,
    IReadOnlyList<string> NotDeclared);

/// <summary>Everything the product screen shows, assembled once.</summary>
public sealed record ProductAnalysisResult(
    ProductRecord Product,
    IReadOnlyList<PanelView> Panels,
    PalmFinding Palm,
    IReadOnlyList<string> PeerComparisons,
    Freshness Freshness,
    string FreshnessNote,
    bool IsComplete,

    /// <summary>The brand's display name, since the record stores a slug.</summary>
    string? BrandName);

/// <summary>
/// Turns a record into what the screen shows, in one place.
/// </summary>
/// <remarks>
/// Assembled here rather than in the view model so the whole answer can be tested without a UI,
/// and so the rules that matter are enforced once. The two that are easy to break by accident:
/// a nutrient the packet does not declare renders as Unknown and never as zero or a blank row,
/// and a record with neither nutrition nor ingredients is marked incomplete so the screen keeps
/// offering discovery for it, which ADR-0026 requires.
/// </remarks>
public sealed class ProductAnalysis(IRuleProvider rules, IAnalysisRepository repository)
{
    private PalmDetection? _palm;

    /// <summary>
    /// The freshness thresholds from DATA_POLICY.md section 5. Kept here as the single place the
    /// app decides what a verification date means.
    /// </summary>
    private const int ReviewAfterMonths = 6;
    private const int OutdatedAfterMonths = 12;

    public async Task<ProductAnalysisResult> AnalyseAsync(
        ProductRecord product,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var ruleSets = await rules.GetRulesAsync(cancellationToken);

        _palm ??= new PalmDetection(await repository.LoadDictionaryAsync(cancellationToken));

        var panels = new List<PanelView>();

        foreach (var block in product.Nutrition ?? [])
        {
            panels.Add(BuildPanel(block, ruleSets));
        }

        var (freshness, note) = Freshness_(product.Verification.LastVerified, today);

        return new ProductAnalysisResult(
            product,
            panels,
            _palm.Detect(product.IngredientsRaw, product.Ingredients),
            await PeerComparisonsAsync(product, cancellationToken),
            freshness,
            note,

            // The same condition the catalogue stores and the importer counts: a record with
            // neither nutrition nor ingredients tells the user almost nothing, and must not look
            // like a complete answer just because the lookup succeeded.
            IsComplete: product.Nutrition is { Count: > 0 }
                        || !string.IsNullOrWhiteSpace(product.IngredientsRaw),

            BrandName: product.Brand is null
                ? null
                : await repository.DisplayNameAsync("brand", product.Brand, cancellationToken)
                  ?? product.Brand);
    }

    private static PanelView BuildPanel(NutritionBlock block, IReadOnlyList<RuleSet> ruleSets)
    {
        var rows = new List<NutrientRow>();

        foreach (var nutrient in Nutrients.PanelOrder)
        {
            var value = Nutrients.Read(block.Values, nutrient);
            var declaredAbsent = block.NotDeclared?.Contains(nutrient) == true;

            // A nutrient nobody has recorded and one the packet states is absent are different
            // facts. Both are shown; neither becomes a zero.
            if (value is null && !declaredAbsent)
            {
                continue;
            }

            rows.Add(new NutrientRow(
                nutrient,
                Nutrients.DisplayName(nutrient),
                value,
                Nutrients.UnitOf(nutrient)));
        }

        var basisLabel = block.Basis switch
        {
            NutritionBasis.Per100g => "Per 100 g",
            NutritionBasis.Per100ml => "Per 100 ml",
            NutritionBasis.PerServing => "Per serving",
            _ => "Per package",
        };

        return new PanelView(
            basisLabel,
            block.Serving?.Description,
            rows,
            IndicatorEngine.Evaluate(block, ruleSets),
            block.NotDeclared ?? []);
    }

    private async Task<IReadOnlyList<string>> PeerComparisonsAsync(
        ProductRecord product,
        CancellationToken cancellationToken)
    {
        // No category means no peers, which is 74 per cent of the catalogue. Silence is the
        // correct output, not a comparison against everything.
        if (product.Category is null || product.Nutrition is null)
        {
            return [];
        }

        var results = new List<string>();

        // The category's display name, falling back to the slug if the catalogue has no record
        // for it. Better a slug than a blank.
        var categoryName =
            await repository.DisplayNameAsync("category", product.Category, cancellationToken)
            ?? product.Category;

        foreach (var block in product.Nutrition)
        {
            var stats = await repository.PeerStatsAsync(product.Category, block.Basis, cancellationToken);

            foreach (var stat in stats)
            {
                if (PeerComparison.Describe(stat, block.Values, categoryName) is { } sentence)
                {
                    results.Add(sentence);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Applies the freshness thresholds from DATA_POLICY.md.
    /// </summary>
    /// <remarks>
    /// The wording matters as much as the threshold. These are prompts to re-check, not claims
    /// that a record is wrong, and the policy says so explicitly.
    /// </remarks>
    private static (Freshness Level, string Note) Freshness_(string lastVerified, DateOnly today)
    {
        if (!DateOnly.TryParse(lastVerified, out var verified))
        {
            return (Freshness.Unknown, "There is no verification date for this record.");
        }

        var months = ((today.Year - verified.Year) * 12) + today.Month - verified.Month;

        if (today.Day < verified.Day)
        {
            months--;
        }

        return months switch
        {
            < ReviewAfterMonths => (Freshness.Current,
                $"Last checked {verified:d MMMM yyyy}."),

            < OutdatedAfterMonths => (Freshness.ReviewRecommended,
                $"Last checked {verified:d MMMM yyyy}, over six months ago. "
                + "Formulations change, so this is worth re-checking against the packet."),

            _ => (Freshness.PossiblyOutdated,
                $"Last checked {verified:d MMMM yyyy}, over a year ago. "
                + "This may not match what is on the shelf now."),
        };
    }
}
