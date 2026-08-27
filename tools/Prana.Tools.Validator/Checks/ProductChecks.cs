using Prana.Core.Barcodes;
using Prana.Core.Model;
using Prana.Core.Rules;

namespace Prana.Tools.Validator.Checks;

/// <summary>
/// Rules about a single product record: identity, nutrition arithmetic and file placement.
/// </summary>
/// <remarks>
/// The arithmetic itself lives in <see cref="NutritionConsistency"/> in Prana.Core, because the
/// app reports the same findings to the user. This class only decides how severely CI reacts
/// to each one.
/// </remarks>
public static class ProductChecks
{
    /// <summary>
    /// How many days after verification a record is worth revisiting. Matches the freshness
    /// thresholds in DATA_POLICY.md.
    /// </summary>
    private const int StaleAfterDays = 365;

    public static IEnumerable<Diagnostic> Check(RecordFile file, ProductRecord product, DateOnly today)
    {
        foreach (var diagnostic in CheckIdentity(file, product))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in CheckLocation(file, product))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in CheckNutrition(file, product))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in CheckIngredients(file, product))
        {
            yield return diagnostic;
        }

        foreach (var diagnostic in CheckVerification(file, product, today))
        {
            yield return diagnostic;
        }
    }

    private static IEnumerable<Diagnostic> CheckIdentity(RecordFile file, ProductRecord product)
    {
        if (!Gtin.HasValidCheckDigit(product.BarcodePrinted))
        {
            yield return file.At(
                Severity.Error,
                Rules.BadCheckDigit,
                "/barcode_printed",
                $"{product.BarcodePrinted} fails its check digit, so it is not a real barcode. "
                    + "The most common cause is a mistyped or misread digit.");

            yield break;
        }

        if (Gtin.TryNormalize(product.BarcodePrinted, out var canonical)
            && !string.Equals(canonical, product.Gtin, StringComparison.Ordinal))
        {
            yield return file.At(
                Severity.Error,
                Rules.GtinNotCanonical,
                "/gtin",
                $"gtin should be {canonical}, the printed barcode padded to 14 digits. "
                    + "Keying on anything else lets the same product be stored twice.");
        }

        var expected = ExpectedLength(product.BarcodeFormat);

        if (expected is { } length && product.BarcodePrinted.Length != length)
        {
            yield return file.At(
                Severity.Warning,
                Rules.BarcodeFormatMismatch,
                "/barcode_format",
                $"barcode_format says {product.BarcodeFormat}, which is {length} digits, "
                    + $"but barcode_printed has {product.BarcodePrinted.Length}.");
        }
    }

    private static int? ExpectedLength(BarcodeFormat format) => format switch
    {
        BarcodeFormat.Ean13 => 13,
        BarcodeFormat.Ean8 => 8,
        BarcodeFormat.UpcA => 12,
        BarcodeFormat.Gtin14 or BarcodeFormat.Itf14 => 14,
        // UPC-E is variable, between 6 and 8 digits depending on how it is written down.
        _ => null,
    };

    private static IEnumerable<Diagnostic> CheckLocation(RecordFile file, ProductRecord product)
    {
        if (product.Gtin.Length != Gtin.CanonicalLength)
        {
            yield break;
        }

        var expected = $"data/{Gtin.RelativePathFor(product.Gtin)}";

        // Example records live outside data/ on purpose, so placement only applies to real data.
        if (!file.RelativePath.StartsWith("data/", StringComparison.Ordinal))
        {
            yield break;
        }

        if (!string.Equals(file.RelativePath, expected, StringComparison.Ordinal))
        {
            yield return file.At(
                Severity.Error,
                Rules.WrongFileLocation,
                "/gtin",
                $"This record belongs at {expected}. A record filed anywhere else is invisible to lookup.");
        }
    }

    private static IEnumerable<Diagnostic> CheckNutrition(RecordFile file, ProductRecord product)
    {
        if (product.Nutrition is not { Count: > 0 })
        {
            yield break;
        }

        var seenBases = new Dictionary<NutritionBasis, int>();

        for (var i = 0; i < product.Nutrition.Count; i++)
        {
            var block = product.Nutrition[i];

            if (seenBases.TryGetValue(block.Basis, out var first))
            {
                yield return file.At(
                    Severity.Error,
                    Rules.DuplicateBasis,
                    $"/nutrition/{i}/basis",
                    $"There is already a {block.Basis} block at index {first}. "
                        + "Two panels for the same basis cannot both be what the packet says.");
            }
            else
            {
                seenBases[block.Basis] = i;
            }

            if (block.Basis == NutritionBasis.PerServing && block.Serving?.Quantity is null)
            {
                yield return file.At(
                    Severity.Warning,
                    Rules.ServingWithoutMass,
                    $"/nutrition/{i}/serving",
                    "This serving has no mass or volume, so these values cannot be compared with "
                        + "any other product. Record the serving size if the packet states one.");
            }
        }

        foreach (var finding in NutritionConsistency.Check(product))
        {
            yield return file.At(SeverityFor(finding.Code), CodeFor(finding.Code), finding.Path, finding.Message);
        }
    }

    /// <summary>
    /// A contradiction is an error. Something that merely looks odd is a warning, because
    /// blocking a real contribution over label rounding would teach contributors to distrust CI.
    /// </summary>
    private static Severity SeverityFor(ConsistencyCode code) => code switch
    {
        ConsistencyCode.EnergyDisagreesWithMacros => Severity.Warning,
        ConsistencyCode.EnergyUnitsDisagree => Severity.Warning,

        // A warning rather than an error, because a salt or bouillon product can legitimately
        // sit near the threshold, and blocking those would be wrong. It is loud enough to be
        // noticed and cheap enough to ignore when it is a genuine salt.
        ConsistencyCode.SodiumImplausible => Severity.Warning,
        _ => Severity.Error,
    };

    private static string CodeFor(ConsistencyCode code) => code switch
    {
        ConsistencyCode.SaturatedFatExceedsFat => Rules.SaturatedFatExceedsFat,
        ConsistencyCode.SugarsExceedCarbohydrate => Rules.SugarsExceedCarbohydrate,
        ConsistencyCode.AddedSugarsExceedSugars => Rules.AddedSugarsExceedSugars,
        ConsistencyCode.TransFatExceedsFat => Rules.TransFatExceedsFat,
        ConsistencyCode.MacrosExceedBasis => Rules.MacrosExceedBasis,
        ConsistencyCode.EnergyDisagreesWithMacros => Rules.EnergyDisagreesWithMacros,
        ConsistencyCode.EnergyUnitsDisagree => Rules.EnergyUnitsDisagree,
        ConsistencyCode.NotDeclaredButPresent => Rules.NotDeclaredButPresent,
        ConsistencyCode.SodiumImplausible => Rules.SodiumImplausible,
        _ => Rules.SchemaViolation,
    };

    private static IEnumerable<Diagnostic> CheckIngredients(RecordFile file, ProductRecord product)
    {
        if (product.Ingredients is { Count: > 0 } && string.IsNullOrWhiteSpace(product.IngredientsRaw))
        {
            yield return file.At(
                Severity.Error,
                Rules.ParsedWithoutRaw,
                "/ingredients",
                "A parsed ingredient list with no ingredients_raw has thrown away its own evidence. "
                    + "The raw statement from the packet is what the parsed tree is derived from.");
        }

        if (product.Ingredients is null)
        {
            yield break;
        }

        foreach (var diagnostic in CheckPercentages(file, product.Ingredients, "/ingredients"))
        {
            yield return diagnostic;
        }
    }

    private static IEnumerable<Diagnostic> CheckPercentages(
        RecordFile file,
        IReadOnlyList<Ingredient> ingredients,
        string pointer)
    {
        var total = ingredients.Sum(i => i.Percentage ?? 0);

        if (total > 100.5)
        {
            yield return file.At(
                Severity.Warning,
                Rules.PercentagesExceedHundred,
                pointer,
                $"The declared percentages at this level add up to {total:0.#}%.");
        }

        for (var i = 0; i < ingredients.Count; i++)
        {
            if (ingredients[i].Children is not { Count: > 0 } children)
            {
                continue;
            }

            foreach (var diagnostic in CheckPercentages(file, children, $"{pointer}/{i}/children"))
            {
                yield return diagnostic;
            }
        }
    }

    private static IEnumerable<Diagnostic> CheckVerification(RecordFile file, ProductRecord product, DateOnly today)
    {
        if (!DateOnly.TryParse(product.Verification.LastVerified, out var verified))
        {
            yield break;
        }

        if (verified > today)
        {
            yield return file.At(
                Severity.Error,
                Rules.VerifiedInTheFuture,
                "/verification/last_verified",
                $"last_verified is {verified:yyyy-MM-dd}, which is in the future.");
        }
        else if (verified.AddDays(StaleAfterDays) < today)
        {
            yield return file.At(
                Severity.Info,
                Rules.StaleVerification,
                "/verification/last_verified",
                $"Last verified {verified:yyyy-MM-dd}, over a year ago. Formulations change, so this "
                    + "is worth re-checking against a current packet.");
        }
    }
}
