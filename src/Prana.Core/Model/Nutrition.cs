using System.Text.Json.Serialization;

namespace Prana.Core.Model;

/// <summary>A measured amount with an explicit unit.</summary>
public sealed class Quantity
{
    [JsonPropertyName("value")]
    public required double Value { get; init; }

    [JsonPropertyName("unit")]
    public required Unit Unit { get; init; }
}

/// <summary>Net quantity as declared on the packet.</summary>
public sealed class PackageInfo
{
    [JsonPropertyName("quantity")]
    public required Quantity Quantity { get; init; }

    /// <summary>Present only when the packet contains several wrapped units and says so.</summary>
    [JsonPropertyName("multipack_count")]
    public int? MultipackCount { get; init; }
}

/// <summary>
/// The serving a per-serving panel refers to. The description is what the packet prints. The
/// quantity is only present when the packet states a serving mass, and without it a
/// per-serving value can never be compared with anything.
/// </summary>
public sealed class ServingInfo
{
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("quantity")]
    public Quantity? Quantity { get; init; }
}

/// <summary>
/// The closed set of nutrients Phase 1 understands. The unit is part of the property name, so
/// a value can never exist without one.
/// </summary>
/// <remarks>
/// A <c>null</c> property means the nutrient is not present in this panel. That is either
/// "the packet does not declare it", which is recorded in
/// <see cref="NutritionBlock.NotDeclared"/>, or "nobody has researched it yet", which is the
/// absence of both. Never write a zero to mean unknown.
/// </remarks>
public sealed class NutritionValues
{
    [JsonPropertyName("energy_kcal")]
    public double? EnergyKcal { get; init; }

    [JsonPropertyName("energy_kj")]
    public double? EnergyKj { get; init; }

    [JsonPropertyName("protein_g")]
    public double? ProteinG { get; init; }

    [JsonPropertyName("carbohydrate_g")]
    public double? CarbohydrateG { get; init; }

    [JsonPropertyName("sugars_g")]
    public double? SugarsG { get; init; }

    [JsonPropertyName("added_sugars_g")]
    public double? AddedSugarsG { get; init; }

    [JsonPropertyName("fat_g")]
    public double? FatG { get; init; }

    [JsonPropertyName("saturated_fat_g")]
    public double? SaturatedFatG { get; init; }

    [JsonPropertyName("trans_fat_g")]
    public double? TransFatG { get; init; }

    [JsonPropertyName("fibre_g")]
    public double? FibreG { get; init; }

    [JsonPropertyName("sodium_mg")]
    public double? SodiumMg { get; init; }
}

/// <summary>
/// One nutrition panel, tied to the basis it was printed against. Blocks are never merged and
/// values are never converted between bases. A packet printing both per 100 g and per serving
/// produces two blocks.
/// </summary>
public sealed class NutritionBlock
{
    [JsonPropertyName("basis")]
    public required NutritionBasis Basis { get; init; }

    /// <summary>Required when <see cref="Basis"/> is <see cref="NutritionBasis.PerServing"/>.</summary>
    [JsonPropertyName("serving")]
    public ServingInfo? Serving { get; init; }

    [JsonPropertyName("values")]
    public required NutritionValues Values { get; init; }

    /// <summary>
    /// Nutrients checked against this panel and confirmed absent from the packet. This is what
    /// separates "we looked and it is not declared" from "nobody has looked yet", and it is
    /// what stops the research automation from re-researching the same gap forever.
    /// </summary>
    [JsonPropertyName("not_declared")]
    public IReadOnlyList<string>? NotDeclared { get; init; }
}
