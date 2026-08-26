using System.Text.Json.Serialization;

namespace Prana.Core.Model;

/// <summary>
/// What a set of nutrition values is measured against. Never inferred, never defaulted.
/// A missing basis is an invalid record, not an assumption to fill in.
/// </summary>
public enum NutritionBasis
{
    [JsonStringEnumMemberName("per_100g")]
    Per100g,

    [JsonStringEnumMemberName("per_100ml")]
    Per100ml,

    [JsonStringEnumMemberName("per_serving")]
    PerServing,

    [JsonStringEnumMemberName("per_package")]
    PerPackage,
}

/// <summary>Units permitted for a declared quantity. There is no implicit unit anywhere.</summary>
public enum Unit
{
    [JsonStringEnumMemberName("g")]
    Gram,

    [JsonStringEnumMemberName("kg")]
    Kilogram,

    [JsonStringEnumMemberName("ml")]
    Millilitre,

    [JsonStringEnumMemberName("l")]
    Litre,

    [JsonStringEnumMemberName("piece")]
    Piece,
}

/// <summary>The barcode symbology printed on the packet, before padding to GTIN-14.</summary>
public enum BarcodeFormat
{
    [JsonStringEnumMemberName("EAN-13")]
    Ean13,

    [JsonStringEnumMemberName("EAN-8")]
    Ean8,

    [JsonStringEnumMemberName("UPC-A")]
    UpcA,

    [JsonStringEnumMemberName("UPC-E")]
    UpcE,

    [JsonStringEnumMemberName("GTIN-14")]
    Gtin14,

    [JsonStringEnumMemberName("ITF-14")]
    Itf14,
}

/// <summary>
/// Evidence quality, ordered from most to least authoritative. Used to rank sources when
/// they disagree, never to discard the weaker one silently.
/// </summary>
public enum SourceType
{
    [JsonStringEnumMemberName("packaging")]
    Packaging,

    [JsonStringEnumMemberName("manufacturer")]
    Manufacturer,

    [JsonStringEnumMemberName("regulator")]
    Regulator,

    [JsonStringEnumMemberName("open_database")]
    OpenDatabase,

    [JsonStringEnumMemberName("retailer")]
    Retailer,

    [JsonStringEnumMemberName("community")]
    Community,
}

/// <summary>
/// How well the evidence supports a value. There is no <c>Unknown</c> member on purpose:
/// an unknown value is not stored at all, so it can never carry a confidence.
/// </summary>
public enum Confidence
{
    [JsonStringEnumMemberName("high")]
    High,

    [JsonStringEnumMemberName("medium")]
    Medium,

    [JsonStringEnumMemberName("low")]
    Low,
}

/// <summary>Whether a record may be presented to the user as trusted catalogue data.</summary>
public enum VerificationStatus
{
    [JsonStringEnumMemberName("verified")]
    Verified,

    [JsonStringEnumMemberName("unverified")]
    Unverified,

    [JsonStringEnumMemberName("disputed")]
    Disputed,
}

/// <summary>What was done about a disagreement between sources.</summary>
public enum ConflictResolution
{
    [JsonStringEnumMemberName("unresolved")]
    Unresolved,

    [JsonStringEnumMemberName("preferred_source")]
    PreferredSource,

    [JsonStringEnumMemberName("human_decision")]
    HumanDecision,
}
