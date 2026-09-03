using System.Globalization;
using Prana.Core.Model;

namespace Prana.Core.Rules;

/// <summary>Where a value sits against a rule set.</summary>
public enum IndicatorLevel
{
    Lower,
    Moderate,
    Higher,

    /// <summary>Above a published category limit. Not a band; a single cut-off was crossed.</summary>
    AboveLimit,

    /// <summary>At or below a published category limit.</summary>
    WithinLimit,
}

/// <summary>
/// One statement the app can make about one nutrient, together with everything needed to
/// justify it on screen.
/// </summary>
/// <remarks>
/// Every field that appears in the explanation sheet is carried here rather than looked up
/// again by the UI. An indicator that cannot say which rule produced it and which version of
/// that rule has no business being displayed, so it is not possible to construct one.
/// </remarks>
public sealed record Indicator
{
    public required string Nutrient { get; init; }

    public required string Display { get; init; }

    public required IndicatorLevel Level { get; init; }

    /// <summary>The value assessed, after any derivation and any scaling to a per-100 basis.</summary>
    public required double Value { get; init; }

    public required string Unit { get; init; }

    public required string RuleId { get; init; }

    public required string RuleVersion { get; init; }

    public required string RuleTitle { get; init; }

    /// <summary>The rule set's own description of what it compares.</summary>
    public required string RuleSummary { get; init; }

    public required RuleSource Source { get; init; }

    /// <summary>Set when the figure was scaled from a per-serving panel rather than printed.</summary>
    public string? CalculatedFrom { get; init; }

    /// <summary>Set when a nutrient was derived from another, for example salt from sodium.</summary>
    public string? DerivedNote { get; init; }

    /// <summary>The sentence shown under the indicator, stating the comparison in full.</summary>
    public required string Statement { get; init; }

    public bool IsCalculated => CalculatedFrom is not null;
}

/// <summary>
/// Turns a nutrition panel and a set of rules into indicators.
/// </summary>
/// <remarks>
/// This lives in Core rather than in the app for the same reason the nutrition consistency
/// checks do: what CI says about a record and what a phone shows for it have to be the same
/// statement, produced by the same code.
///
/// The engine makes comparative statements only. It never totals nutrients into a score, never
/// ranks products against each other as better or worse, and never says a food is healthy. Those
/// are prohibited by DATA_POLICY.md section 7, and the shape of this type is what keeps them out:
/// there is nowhere to put a verdict.
/// </remarks>
public static class IndicatorEngine
{
    /// <summary>
    /// Evaluates every band a rule set defines against one panel.
    /// </summary>
    /// <param name="block">The panel as declared on the packet.</param>
    /// <param name="ruleSets">
    /// Candidate rule sets. The one matching the panel's basis and form is used; others are
    /// skipped, which is what stops drink thresholds being applied to a biscuit.
    /// </param>
    public static IReadOnlyList<Indicator> Evaluate(
        NutritionBlock block,
        IEnumerable<RuleSet> ruleSets)
    {
        var panel = PanelBasis.Normalise(block);

        if (panel is null)
        {
            return [];
        }

        var results = new List<Indicator>();

        foreach (var rules in ruleSets)
        {
            if (rules.Kind != RuleKind.NutrientBands || rules.Bands is null)
            {
                continue;
            }

            // A rule set states which panels it may be used on, and this is the only place that
            // is enforced. Applying the food bands to a drink would call almost every soft drink
            // low in sugar.
            if (rules.AppliesTo is not { } applies
                || applies.Basis != panel.Basis
                || applies.Form != panel.Form)
            {
                continue;
            }

            foreach (var band in rules.Bands)
            {
                if (Evaluate(block.Values, panel, rules, band) is { } indicator)
                {
                    results.Add(indicator);
                }
            }
        }

        return results;
    }

    private static Indicator? Evaluate(
        NutritionValues values,
        NormalisedPanel panel,
        RuleSet rules,
        Band band)
    {
        double? value;
        string? derivedNote = null;

        if (band.DerivedFrom is { } derivation)
        {
            var from = panel.Read(values, derivation.Nutrient);
            value = from is null ? null : from.Value * derivation.MultiplyBy;
            derivedNote = derivation.Note;
        }
        else
        {
            value = panel.Read(values, band.Nutrient);
        }

        // Not declared on the packet. The screen says unknown; it does not say zero, and it does
        // not quietly omit the row. See the unknown model in docs/PRODUCT_SCHEMA.md.
        if (value is null)
        {
            return null;
        }

        var level = Level(value.Value, band);

        // Some sources add a second Higher test on the amount in one portion, which catches a
        // modest concentration served in a large quantity.
        if (level != IndicatorLevel.Higher
            && band.PortionHigherAbove is { } portionLimit
            && band.PortionAppliesAbove is { } appliesAbove
            && panel.ServingAmount is { } servingAmount
            && servingAmount > appliesAbove)
        {
            var perPortion = value.Value * servingAmount / 100.0;

            if (perPortion > portionLimit)
            {
                level = IndicatorLevel.Higher;
            }
        }

        return new Indicator
        {
            Nutrient = band.Nutrient,
            Display = band.Display,
            Level = level,
            Value = value.Value,
            Unit = band.Unit,
            RuleId = rules.Id,
            RuleVersion = rules.Version,
            RuleTitle = rules.Title,
            RuleSummary = rules.Summary,
            Source = rules.Source,
            CalculatedFrom = panel.Calculated ? panel.ServingDescription ?? "the stated serving" : null,
            DerivedNote = derivedNote,
            Statement = Statement(band, level, value.Value, panel),
        };
    }

    /// <summary>
    /// Places a value in a band. The boundaries follow the published wording exactly: at or below
    /// the low cut-off is Lower, strictly above the high cut-off is Higher. Getting these the
    /// wrong way round would move every product sitting exactly on a threshold.
    /// </summary>
    private static IndicatorLevel Level(double value, Band band) =>
        value <= band.LowerAtOrBelow ? IndicatorLevel.Lower
        : value > band.HigherAbove ? IndicatorLevel.Higher
        : IndicatorLevel.Moderate;

    private static string Statement(
        Band band,
        IndicatorLevel level,
        double value,
        NormalisedPanel panel)
    {
        var per = panel.Basis == RuleBasis.Per100ml ? "100 ml" : "100 g";
        var amount = Format(value) + " " + band.Unit;

        var comparison = level switch
        {
            IndicatorLevel.Lower =>
                $"at or below {Format(band.LowerAtOrBelow)} {band.Unit}, which this rule calls lower",
            IndicatorLevel.Higher =>
                $"above {Format(band.HigherAbove)} {band.Unit}, which this rule calls higher",
            _ =>
                $"between {Format(band.LowerAtOrBelow)} and {Format(band.HigherAbove)} {band.Unit}, "
                + "which this rule calls moderate",
        };

        var sentence = $"{amount} per {per}: {comparison}.";

        return panel.Calculated
            ? sentence + $" Calculated from the declared serving of {panel.ServingDescription}, "
                       + "not printed on the packet."
            : sentence;
    }

    /// <summary>
    /// Formats a number the way a label would, without trailing zeros. A threshold written as
    /// 22.5 should read as 22.5 and one written as 5 should read as 5.
    /// </summary>
    internal static string Format(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
