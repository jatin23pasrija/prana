using System.Globalization;
using System.Text;
using Prana.Core.Barcodes;
using Prana.Core.Model;

namespace Prana.Tools.Importer.Sources.OpenFoodFacts;

/// <summary>
/// Turns one Open Food Facts row into a Prana record, or explains why it cannot.
/// </summary>
/// <remarks>
/// The mapping is where an import either respects the data rules or quietly launders a
/// violation of them. Two places matter most.
///
/// First, nutrition basis. Open Food Facts publishes <c>*_100g</c> columns for every product,
/// including ones whose label only ever stated a per-serving panel, by dividing through by the
/// serving size. That is exactly the silent conversion DATA_POLICY.md forbids. So this mapper
/// reads <c>nutrition_data_per</c> and takes the columns that match what the packet actually
/// declared, rather than the convenient normalised ones.
///
/// Second, units. Open Food Facts stores sodium in grams. Prana stores milligrams. The
/// conversion is explicit and in one place, because an unnoticed factor of a thousand on sodium
/// would be worse than having no sodium data at all.
/// </remarks>
public sealed class OffMapper(DateOnly retrievedAt, string licence)
{
    /// <summary>GS1 prefix issued to members registered in India.</summary>
    private const string IndiaBarcodePrefix = "890";

    private const string SourceId = "s1";

    public bool TryMap(OffRow row, out ProductRecord? product, out string? dropReason)
    {
        product = null;
        dropReason = null;

        var printed = (row["code"] ?? string.Empty).Trim();

        if (!Gtin.TryNormalize(printed, out var gtin))
        {
            dropReason = "barcode is missing, malformed, or fails its check digit";
            return false;
        }

        if (!IsIndian(row, printed))
        {
            dropReason = "not sold in India and not an Indian barcode";
            return false;
        }

        var name = Clean(row["product_name"]);

        if (name is null || name.Length < 2)
        {
            dropReason = "no product name";
            return false;
        }

        var nutrition = ReadNutrition(row);
        var ingredientsRaw = Clean(row["ingredients_text"]);

        // The quality bar: a record has to tell the user something beyond the fact that the
        // barcode exists. A name with no nutrition and no ingredients is not worth shipping to
        // a phone, and it would dilute a catalogue people are meant to trust.
        if (nutrition is null && ingredientsRaw is null)
        {
            dropReason = "no nutrition and no ingredients";
            return false;
        }

        var brand = SlugOf(FirstOf(row["brands"]));

        product = new ProductRecord
        {
            SchemaVersion = 1,
            Gtin = gtin,
            BarcodePrinted = printed,
            BarcodeFormat = FormatFor(printed),
            Name = Truncate(name, 200),
            Brand = brand,
            Category = CategoryMap.Map(row["categories_tags"]),
            Countries = ["IN"],
            Package = ReadPackage(row),
            Nutrition = nutrition is null ? null : [nutrition],
            IngredientsRaw = ingredientsRaw is null ? null : Truncate(ingredientsRaw, 4000),
            Sources =
            [
                new Source
                {
                    Id = SourceId,
                    Type = SourceType.OpenDatabase,
                    Url = $"https://world.openfoodfacts.org/product/{printed}",
                    RetrievedAt = retrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Licence = licence,
                },
            ],
            Provenance = BuildProvenance(name: true, brand: brand is not null,
                category: CategoryMap.Map(row["categories_tags"]) is not null,
                package: ReadPackage(row) is not null,
                nutrition: nutrition is not null,
                ingredients: ingredientsRaw is not null),
            Verification = new Verification
            {
                // Open Food Facts is community-entered. Calling it verified would make our own
                // verification vocabulary meaningless, and the app is built to display honest
                // uncertainty rather than hide it.
                Status = VerificationStatus.Unverified,
                LastVerified = retrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            },
        };

        return true;
    }

    /// <summary>
    /// Whether this product belongs in an India-first catalogue.
    /// </summary>
    /// <remarks>
    /// The country tag alone is not enough. It is entered by contributors and is missing on many
    /// records, so relying on it loses real Indian products. A barcode beginning 890 was issued
    /// by GS1 India, which is strong evidence even when nobody tagged the country. Taking either
    /// signal finds products the country filter alone would miss.
    /// </remarks>
    private static bool IsIndian(OffRow row, string printed)
    {
        var countries = row["countries_tags"] ?? string.Empty;

        if (countries.Contains("en:india", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return printed.StartsWith(IndiaBarcodePrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads the panel the label actually declared, never the normalised one.
    /// </summary>
    private NutritionBlock? ReadNutrition(OffRow row)
    {
        var declaredPer = (row["nutrition_data_per"] ?? string.Empty).Trim().ToLowerInvariant();

        NutritionBasis basis;
        string suffix;
        ServingInfo? serving = null;

        switch (declaredPer)
        {
            case "100g":
                basis = NutritionBasis.Per100g;
                suffix = "_100g";
                break;

            case "100ml":
                // Open Food Facts stores per-100ml figures in the same columns it uses for
                // per-100g. Only the declared basis distinguishes them.
                basis = NutritionBasis.Per100ml;
                suffix = "_100g";
                break;

            case "serving":
                basis = NutritionBasis.PerServing;
                suffix = "_serving";
                serving = ReadServing(row);
                break;

            default:
                // The source did not say what the numbers are measured against, so neither can
                // we. Assuming per 100 g here would be the silent conversion the data policy
                // forbids, applied to every product at once where nobody would ever see it.
                return null;
        }

        // A per-serving panel with no serving described cannot be recorded honestly: there would
        // be no way to say what the numbers are per.
        if (basis == NutritionBasis.PerServing && serving is null)
        {
            return null;
        }

        var values = new NutritionValues
        {
            EnergyKcal = Round(Number(row[$"energy-kcal{suffix}"])),
            EnergyKj = Round(Number(row[$"energy-kj{suffix}"])),
            ProteinG = Round(Number(row[$"proteins{suffix}"])),
            CarbohydrateG = Round(Number(row[$"carbohydrates{suffix}"])),
            SugarsG = Round(Number(row[$"sugars{suffix}"])),
            FatG = Round(Number(row[$"fat{suffix}"])),
            SaturatedFatG = Round(Number(row[$"saturated-fat{suffix}"])),
            TransFatG = Round(Number(row[$"trans-fat{suffix}"])),
            FibreG = Round(Number(row[$"fiber{suffix}"])),

            // Open Food Facts stores sodium in grams. Prana stores milligrams.
            // The conversion happens before rounding, not after: 0.296 g is 296 mg, but rounding
            // 0.296 to two places first gives 0.3, and then 300 mg. That is a quiet 1.4% error
            // on every sodium figure in the catalogue.
            SodiumMg = Round(Number(row[$"sodium{suffix}"]) * 1000),
        };

        var declared = Prana.Core.Rules.NutritionConsistency.DeclaredFieldNames(values);

        return declared.Count == 0
            ? null
            : new NutritionBlock { Basis = basis, Serving = serving, Values = values };
    }

    private static ServingInfo? ReadServing(OffRow row)
    {
        var description = Clean(row["serving_size"]);

        if (description is null)
        {
            return null;
        }

        var quantity = Number(row["serving_quantity"]);

        return new ServingInfo
        {
            Description = Truncate(description, 100),

            // serving_quantity is grams in Open Food Facts. A serving described in millilitres
            // still records its quantity in grams there, so the unit is not guessed from the text.
            Quantity = quantity is > 0 ? new Quantity { Value = quantity.Value, Unit = Unit.Gram } : null,
        };
    }

    private static PackageInfo? ReadPackage(OffRow row)
    {
        var quantity = Number(row["product_quantity"]);

        if (quantity is not > 0)
        {
            return null;
        }

        var unit = (row["product_quantity_unit"] ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ml" or "l" => Unit.Millilitre,
            _ => Unit.Gram,
        };

        return new PackageInfo { Quantity = new Quantity { Value = quantity.Value, Unit = unit } };
    }

    /// <summary>
    /// Builds the coverage map. Only fields actually present are claimed, because a provenance
    /// entry for a field that does not exist backs nothing and the validator says so.
    /// </summary>
    private static Dictionary<string, ProvenanceEntry> BuildProvenance(
        bool name, bool brand, bool category, bool package, bool nutrition, bool ingredients)
    {
        // Medium, never high. One community database agreeing with itself is not corroboration,
        // and high confidence is what makes a record eligible for automatic merge later.
        var entry = new ProvenanceEntry { Source = SourceId, Confidence = Confidence.Medium };
        var map = new Dictionary<string, ProvenanceEntry>(StringComparer.Ordinal);

        if (name)
        {
            map["name"] = entry;
        }

        if (brand)
        {
            map["brand"] = entry;
        }

        if (category)
        {
            map["category"] = entry;
        }

        if (package)
        {
            map["package"] = entry;
        }

        if (nutrition)
        {
            map["nutrition"] = entry;
        }

        if (ingredients)
        {
            map["ingredients_raw"] = entry;
        }

        return map;
    }

    private static BarcodeFormat FormatFor(string printed) => printed.Length switch
    {
        8 => BarcodeFormat.Ean8,
        12 => BarcodeFormat.UpcA,
        14 => BarcodeFormat.Gtin14,
        _ => BarcodeFormat.Ean13,
    };

    private static double? Number(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        // Open Food Facts contains negative and absurd values from bad contributions.
        return value < 0 || double.IsNaN(value) || double.IsInfinity(value) ? null : value;
    }

    /// <summary>
    /// Rounds a stored value to two decimal places. Applied at the point of storage, after any
    /// unit conversion, so that converting never compounds a rounding error. Rounding at all is
    /// what keeps a re-import byte-identical, since floating point text is otherwise unstable.
    /// </summary>
    private static double? Round(double? value) =>
        value is null ? null : Math.Round(value.Value, 2, MidpointRounding.AwayFromZero);

    private static string? Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Contributed text carries newlines and stray whitespace that would make every record
        // differ from itself between imports.
        var collapsed = string.Join(' ', raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string? FirstOf(string? commaSeparated) =>
        Clean(commaSeparated)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max].TrimEnd();

    /// <summary>Turns free text into a slug the schema will accept, or null when nothing survives.</summary>
    public static string? SlugOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalised = value.Trim().ToLowerInvariant();
        var builder = new StringBuilder(normalised.Length);
        var lastWasHyphen = true;

        foreach (var c in normalised)
        {
            var folded = Fold(c);

            if (folded is not null)
            {
                builder.Append(folded);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen)
            {
                builder.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        return slug.Length is 0 or > 80 ? null : slug;
    }

    /// <summary>
    /// Maps one character to its ASCII form, or null when it is not part of a slug.
    /// </summary>
    /// <remarks>
    /// The obvious implementation is <c>Normalize(NormalizationForm.FormD)</c> followed by
    /// dropping combining marks. It does not work here. This repository builds with
    /// <c>InvariantGlobalization</c>, and in invariant mode <c>Normalize</c> is a no-op: it
    /// returns the string unchanged, so an accented character never decomposes and simply
    /// becomes a separator. Nestlé silently became "nestl".
    ///
    /// So the folding is explicit. It covers the accented Latin letters that appear in brand
    /// names sold in India. Anything outside it, including Devanagari and other Indic scripts,
    /// becomes a separator, which means a purely non-Latin brand name yields no slug at all.
    /// That is the honest outcome: we would rather have no brand reference than a mangled one.
    /// </remarks>
    private static string? Fold(char c)
    {
        if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
        {
            return c.ToString();
        }

        return c switch
        {
            'á' or 'à' or 'â' or 'ä' or 'ã' or 'å' or 'ā' => "a",
            'é' or 'è' or 'ê' or 'ë' or 'ē' => "e",
            'í' or 'ì' or 'î' or 'ï' or 'ī' => "i",
            'ó' or 'ò' or 'ô' or 'ö' or 'õ' or 'ō' => "o",
            'ú' or 'ù' or 'û' or 'ü' or 'ū' => "u",
            'ñ' => "n",
            'ç' => "c",
            'ý' or 'ÿ' => "y",
            'š' => "s",
            'ž' => "z",
            'ø' => "o",
            'æ' => "ae",
            'œ' => "oe",
            'ß' => "ss",
            _ => null,
        };
    }
}
