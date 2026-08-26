using System.Text.Json.Serialization;

namespace Prana.Core.Model;

/// <summary>
/// A canonical ingredient with its aliases. This is what turns palmolein oil, palm olein and
/// refined palm oil on three different packets into one attribute the app can reason about.
/// Stored at <c>data/ingredients/{id}.json</c>.
/// </summary>
public sealed class IngredientRecord
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("names")]
    public IReadOnlyDictionary<string, string>? Names { get; init; }

    /// <summary>Label wordings that mean this ingredient. Added when seen on a real packet, not speculatively.</summary>
    [JsonPropertyName("aliases")]
    public IReadOnlyList<string>? Aliases { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("flags")]
    public IReadOnlyList<string>? Flags { get; init; }

    /// <summary>Plain English and factual. What it is and why it is on the label, not whether it is good for you.</summary>
    [JsonPropertyName("explanation")]
    public string? Explanation { get; init; }

    [JsonPropertyName("country_notes")]
    public IReadOnlyDictionary<string, string>? CountryNotes { get; init; }

    [JsonPropertyName("sources")]
    public required IReadOnlyList<Source> Sources { get; init; }

    [JsonPropertyName("provenance")]
    public required IReadOnlyDictionary<string, ProvenanceEntry> Provenance { get; init; }
}

/// <summary>
/// A brand as printed on packaging. Separate from products so a rename or an ownership change
/// is one edit rather than thousands. Stored at <c>data/brands/{id}.json</c>.
/// </summary>
public sealed class BrandRecord
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("names")]
    public IReadOnlyDictionary<string, string>? Names { get; init; }

    [JsonPropertyName("aliases")]
    public IReadOnlyList<string>? Aliases { get; init; }

    /// <summary>Recorded for transparency. Never used to rank or judge a product.</summary>
    [JsonPropertyName("owner")]
    public string? Owner { get; init; }

    [JsonPropertyName("countries")]
    public IReadOnlyList<string>? Countries { get; init; }

    [JsonPropertyName("website")]
    public string? Website { get; init; }

    [JsonPropertyName("sources")]
    public required IReadOnlyList<Source> Sources { get; init; }

    [JsonPropertyName("provenance")]
    public required IReadOnlyDictionary<string, ProvenanceEntry> Provenance { get; init; }
}

/// <summary>
/// A product category. Categories exist mainly so the alternatives engine can tell what a
/// reasonable substitute is. Stored at <c>data/categories/{id}.json</c>.
/// </summary>
public sealed class CategoryRecord
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("names")]
    public IReadOnlyDictionary<string, string>? Names { get; init; }

    [JsonPropertyName("parent")]
    public string? Parent { get; init; }

    /// <summary>Categories whose products can reasonably replace this one. Deliberately conservative.</summary>
    [JsonPropertyName("substitutable_with")]
    public IReadOnlyList<string>? SubstitutableWith { get; init; }

    /// <summary>
    /// The nutrients worth comparing within this category. Sodium matters for a namkeen and
    /// sugar matters for a biscuit. Comparing everything on everything produces noise.
    /// </summary>
    [JsonPropertyName("relevant_nutrients")]
    public IReadOnlyList<string>? RelevantNutrients { get; init; }

    [JsonPropertyName("typical_basis")]
    public required NutritionBasis TypicalBasis { get; init; }
}

/// <summary>
/// Country-specific labelling conventions. This is the record a fork replaces to target a
/// different country. Stored at <c>data/countries/{code}.json</c>.
/// </summary>
public sealed class CountryRecord
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// GS1 prefixes normally issued in this country. A hint for identification only. A prefix
    /// does not prove where a product was made and is never presented as if it did.
    /// </summary>
    [JsonPropertyName("barcode_prefixes")]
    public IReadOnlyList<string>? BarcodePrefixes { get; init; }

    [JsonPropertyName("default_nutrition_basis")]
    public required NutritionBasis DefaultNutritionBasis { get; init; }

    /// <summary>
    /// Indian labels usually declare sodium, European labels usually declare salt. Converting
    /// between them is arithmetic that is only permitted when we know which one was stated.
    /// </summary>
    [JsonPropertyName("sodium_declared_as")]
    public required string SodiumDeclaredAs { get; init; }

    [JsonPropertyName("languages")]
    public IReadOnlyList<string>? Languages { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}

/// <summary>One curated substitution suggestion, with the specific comparative reasons for it.</summary>
public sealed class AlternativeSuggestion
{
    [JsonPropertyName("gtin")]
    public required string Gtin { get; init; }

    /// <summary>
    /// Always comparative and always specific. There is deliberately no generic "healthier"
    /// reason, because that is a claim this project does not make.
    /// </summary>
    [JsonPropertyName("reasons")]
    public required IReadOnlyList<string> Reasons { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// Hand-curated substitutions for one product. The engine computes suggestions on the device.
/// These records exist only where computation gets it wrong, or where a person knows something
/// the numbers do not show. Stored at <c>data/alternatives/{gtin}.json</c>.
/// </summary>
public sealed class AlternativeRecord
{
    [JsonPropertyName("schema_version")]
    public required int SchemaVersion { get; init; }

    [JsonPropertyName("gtin")]
    public required string Gtin { get; init; }

    [JsonPropertyName("alternatives")]
    public IReadOnlyList<AlternativeSuggestion>? Alternatives { get; init; }

    /// <summary>
    /// Products the engine keeps suggesting that are not reasonable substitutes. An explicit
    /// deny list beats weakening the ranking rules for everyone.
    /// </summary>
    [JsonPropertyName("blocked")]
    public IReadOnlyList<string>? Blocked { get; init; }

    [JsonPropertyName("sources")]
    public IReadOnlyList<Source>? Sources { get; init; }
}
