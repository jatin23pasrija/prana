using System.Text.Json.Serialization;

namespace Prana.Core.Rules;

/// <summary>What a rule set does.</summary>
public enum RuleKind
{
    [JsonStringEnumMemberName("nutrient_bands")]
    NutrientBands,

    [JsonStringEnumMemberName("category_limits")]
    CategoryLimits,

    [JsonStringEnumMemberName("peer_comparison")]
    PeerComparison,
}

/// <summary>Whether a rule set was written for solid foods or for drinks.</summary>
public enum ProductForm
{
    [JsonStringEnumMemberName("food")]
    Food,

    [JsonStringEnumMemberName("drink")]
    Drink,
}

/// <summary>The basis a rule set's thresholds are expressed in.</summary>
public enum RuleBasis
{
    [JsonStringEnumMemberName("per_100g")]
    Per100g,

    [JsonStringEnumMemberName("per_100ml")]
    Per100ml,
}

/// <summary>Where a rule set's numbers were transcribed from.</summary>
/// <remarks>
/// Required by the schema and surfaced in the app, because a threshold without a citation is an
/// invented number, and the whole promise of this feature is that a figure on the screen can be
/// traced back to a document someone else can read.
/// </remarks>
public sealed class RuleSource
{
    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("publisher")]
    public required string Publisher { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("published")]
    public string? Published { get; init; }

    [JsonPropertyName("licence")]
    public required string Licence { get; init; }

    [JsonPropertyName("locator")]
    public required string Locator { get; init; }

    [JsonPropertyName("retrieved_at")]
    public required string RetrievedAt { get; init; }
}

/// <summary>How to reach a nutrient no record stores, from one that is stored.</summary>
public sealed class Derivation
{
    [JsonPropertyName("nutrient")]
    public required string Nutrient { get; init; }

    [JsonPropertyName("multiply_by")]
    public required double MultiplyBy { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }
}

/// <summary>Cut-offs placing one nutrient in Lower, Moderate or Higher.</summary>
public sealed class Band
{
    [JsonPropertyName("nutrient")]
    public required string Nutrient { get; init; }

    [JsonPropertyName("display")]
    public required string Display { get; init; }

    [JsonPropertyName("unit")]
    public required string Unit { get; init; }

    [JsonPropertyName("derived_from")]
    public Derivation? DerivedFrom { get; init; }

    [JsonPropertyName("lower_at_or_below")]
    public required double LowerAtOrBelow { get; init; }

    [JsonPropertyName("higher_above")]
    public required double HigherAbove { get; init; }

    [JsonPropertyName("portion_higher_above")]
    public double? PortionHigherAbove { get; init; }

    [JsonPropertyName("portion_applies_above")]
    public double? PortionAppliesAbove { get; init; }
}

/// <summary>One published limit for one nutrient in one category.</summary>
public sealed class CategoryLimitValue
{
    [JsonPropertyName("nutrient")]
    public required string Nutrient { get; init; }

    [JsonPropertyName("display")]
    public required string Display { get; init; }

    [JsonPropertyName("unit")]
    public required string Unit { get; init; }

    [JsonPropertyName("derived_from")]
    public Derivation? DerivedFrom { get; init; }

    [JsonPropertyName("above")]
    public required double Above { get; init; }
}

/// <summary>The published limits for one category.</summary>
public sealed class CategoryLimit
{
    [JsonPropertyName("category_id")]
    public required string CategoryId { get; init; }

    [JsonPropertyName("source_category")]
    public required string SourceCategory { get; init; }

    [JsonPropertyName("limits")]
    public required IReadOnlyList<CategoryLimitValue> Limits { get; init; }
}

/// <summary>Settings for ranking a value against other products in the same category.</summary>
public sealed class PeerComparisonSettings
{
    [JsonPropertyName("minimum_peers")]
    public required int MinimumPeers { get; init; }

    [JsonPropertyName("lower_percentile")]
    public required double LowerPercentile { get; init; }

    [JsonPropertyName("higher_percentile")]
    public required double HigherPercentile { get; init; }

    [JsonPropertyName("nutrients")]
    public required IReadOnlyList<string> Nutrients { get; init; }
}

/// <summary>Which panels a rule set may be applied to.</summary>
public sealed class RuleApplicability
{
    [JsonPropertyName("basis")]
    public required RuleBasis Basis { get; init; }

    [JsonPropertyName("form")]
    public required ProductForm Form { get; init; }
}

/// <summary>
/// One versioned, citable rule set, loaded from a file under <c>rules/</c>.
/// </summary>
/// <remarks>
/// This type is deliberately a faithful mirror of rule.schema.json rather than something more
/// convenient. Thresholds live in data so that changing one is a reviewable diff against a cited
/// document, and the moment the code starts reshaping them on load, that reviewability is gone.
/// </remarks>
public sealed class RuleSet
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("version")]
    public required string Version { get; init; }

    [JsonPropertyName("title")]
    public required string Title { get; init; }

    [JsonPropertyName("kind")]
    public required RuleKind Kind { get; init; }

    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    [JsonPropertyName("applies_to")]
    public RuleApplicability? AppliesTo { get; init; }

    [JsonPropertyName("source")]
    public required RuleSource Source { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("bands")]
    public IReadOnlyList<Band>? Bands { get; init; }

    [JsonPropertyName("limits")]
    public IReadOnlyList<CategoryLimit>? Limits { get; init; }

    [JsonPropertyName("peer_comparison")]
    public PeerComparisonSettings? PeerComparison { get; init; }

    /// <summary>How this rule set is named on screen, for example "Sugars, per 100 g (v1.0.0)".</summary>
    public string Attribution => $"{Title} v{Version}";
}
