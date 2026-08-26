using System.Text.Json.Serialization;

namespace Prana.Core.Model;

/// <summary>
/// One ingredient as declared on the label. Nesting is preserved, because a label stating that
/// chocolate contains sugar is not the same statement as a flat list containing both.
/// </summary>
public sealed class Ingredient
{
    /// <summary>The exact wording from the packet. Never normalised, never corrected.</summary>
    [JsonPropertyName("raw")]
    public required string Raw { get; init; }

    /// <summary>
    /// Reference to a record in <c>data/ingredients</c>. Absent when the raw text has not been
    /// matched to a canonical ingredient yet, which is a normal state rather than an error.
    /// </summary>
    [JsonPropertyName("canonical")]
    public string? Canonical { get; init; }

    /// <summary>Only when the packet declares a percentage. Never derived, never estimated.</summary>
    [JsonPropertyName("percentage")]
    public double? Percentage { get; init; }

    /// <summary>Sub-ingredients declared inside brackets.</summary>
    [JsonPropertyName("children")]
    public IReadOnlyList<Ingredient>? Children { get; init; }
}

/// <summary>
/// One packaged product. Stored at
/// <c>data/products/{first 3 digits of printed barcode}/{gtin}.json</c>.
/// </summary>
/// <remarks>
/// Property order here matches the order fields appear in the on-disk record, because
/// System.Text.Json writes members in declaration order and the round-trip tests require the
/// output to be byte-identical to the input.
/// </remarks>
public sealed class ProductRecord
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    /// <summary>
    /// Canonical key: the barcode zero-padded to 14 digits. Padding is what stops the same
    /// product being stored twice, once as UPC-A and once as the EAN-13 that is the same
    /// number with a leading zero.
    /// </summary>
    [JsonPropertyName("gtin")]
    public required string Gtin { get; init; }

    /// <summary>The digits exactly as printed on the packet, so the app can show what the user is looking at.</summary>
    [JsonPropertyName("barcode_printed")]
    public required string BarcodePrinted { get; init; }

    [JsonPropertyName("barcode_format")]
    public required BarcodeFormat BarcodeFormat { get; init; }

    /// <summary>The name as printed on the front of the packet, including the variant.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Optional translations. Empty until Phase 2. Never required for lookup or analysis.</summary>
    [JsonPropertyName("names")]
    public IReadOnlyDictionary<string, string>? Names { get; init; }

    [JsonPropertyName("brand")]
    public string? Brand { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("countries")]
    public required IReadOnlyList<string> Countries { get; init; }

    [JsonPropertyName("package")]
    public PackageInfo? Package { get; init; }

    [JsonPropertyName("nutrition")]
    public IReadOnlyList<NutritionBlock>? Nutrition { get; init; }

    /// <summary>
    /// The complete ingredient statement copied verbatim from the packet. This is the evidence.
    /// <see cref="Ingredients"/> is derived from it and can be rebuilt at any time.
    /// </summary>
    [JsonPropertyName("ingredients_raw")]
    public string? IngredientsRaw { get; init; }

    [JsonPropertyName("ingredients")]
    public IReadOnlyList<Ingredient>? Ingredients { get; init; }

    [JsonPropertyName("sources")]
    public required IReadOnlyList<Source> Sources { get; init; }

    /// <summary>
    /// Maps paths in this record to the evidence behind them. A path covers everything beneath
    /// it, so one entry for <c>nutrition</c> backs the whole panel. The validator rejects any
    /// published value that no path covers.
    /// </summary>
    [JsonPropertyName("provenance")]
    public required IReadOnlyDictionary<string, ProvenanceEntry> Provenance { get; init; }

    [JsonPropertyName("conflicts")]
    public IReadOnlyList<Conflict>? Conflicts { get; init; }

    [JsonPropertyName("verification")]
    public required Verification Verification { get; init; }
}
