using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Tools.Validator;

namespace Prana.Tools.Validator.Tests;

/// <summary>
/// Builds a record, writes it where the validator expects to find it, and runs the real checks
/// over it.
/// </summary>
/// <remarks>
/// Rules are tested through the whole tool rather than by calling each check directly. A rule
/// that works in isolation but never fires because the orchestrator skipped it is exactly the
/// kind of gap that lets bad data through, and only an end-to-end harness catches that.
/// </remarks>
internal sealed class ValidatorHarness : IDisposable
{
    private readonly string _root;

    public ValidatorHarness()
    {
        _root = Path.Combine(Path.GetTempPath(), "prana-validator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SeedReferenceData();
    }

    /// <summary>
    /// A minimal reference set, so a test tree looks like the real one. Without India present,
    /// every single test would carry an unrelated "no country record" warning, and a baseline
    /// that is never clean is a baseline nobody trusts.
    /// </summary>
    private void SeedReferenceData() => Write("data/countries/IN.json", PranaJson.Serialize(new CountryRecord
    {
        SchemaVersion = 1,
        Code = "IN",
        Name = "India",
        BarcodePrefixes = ["890"],
        DefaultNutritionBasis = NutritionBasis.Per100g,
        SodiumDeclaredAs = "sodium",
    }));

    /// <summary>The real schema directory, so tests cannot drift from what CI enforces.</summary>
    private static string SchemaDirectory { get; } = FindSchemaDirectory();

    /// <summary>Writes a product where its key says it belongs, then validates the tree.</summary>
    public IReadOnlyList<Diagnostic> Validate(ProductRecord product, bool strict = false)
    {
        Write($"data/products/{Core.Barcodes.Gtin.ShardFor(product.Gtin)}/{product.Gtin}.json",
            PranaJson.Serialize(product));

        return Run(strict);
    }

    /// <summary>Writes raw text, for the cases where the point is that the file is malformed.</summary>
    public void Write(string relativePath, string content)
    {
        var absolute = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);
    }

    public IReadOnlyList<Diagnostic> Run(bool strict = false)
    {
        var run = new ValidationRun(new ValidationOptions
        {
            RepositoryRoot = _root,
            SchemaDirectory = SchemaDirectory,
            Paths = [Path.Combine(_root, "data")],
            Strict = strict,
        });

        return run.Execute().Diagnostics;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private static string FindSchemaDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "schema");

            if (File.Exists(Path.Combine(directory.FullName, "Prana.sln")) && Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the schema directory.");
    }
}

/// <summary>
/// A product that passes every rule, with one piece swapped out per test.
/// </summary>
/// <remarks>
/// Each test changes exactly one thing about a known-good record. That way a failing test names
/// the rule that broke rather than a record that was wrong in several ways at once.
/// </remarks>
internal static class ValidProduct
{
    public const string Gtin = "08901234567890";
    public const string Printed = "8901234567890";

    public static ProductRecord Build(
        string? gtin = null,
        string? printed = null,
        BarcodeFormat format = BarcodeFormat.Ean13,
        IReadOnlyList<NutritionBlock>? nutrition = null,
        string? ingredientsRaw = null,
        IReadOnlyList<Ingredient>? ingredients = null,
        IReadOnlyList<Source>? sources = null,
        IReadOnlyDictionary<string, ProvenanceEntry>? provenance = null,
        IReadOnlyList<Conflict>? conflicts = null,
        Verification? verification = null) => new()
        {
            SchemaVersion = 1,
            Gtin = gtin ?? Gtin,
            BarcodePrinted = printed ?? Printed,
            BarcodeFormat = format,
            Name = "Example Test Biscuits",
            Countries = ["IN"],
            Nutrition = nutrition,
            IngredientsRaw = ingredientsRaw,
            Ingredients = ingredients,
            Sources = sources ?? [OneSource],
            Provenance = provenance ?? new Dictionary<string, ProvenanceEntry>
            {
                ["name"] = new() { Source = "s1", Confidence = Confidence.High },
            },
            Conflicts = conflicts,
            Verification = verification ?? new Verification
            {
                Status = VerificationStatus.Unverified,
                LastVerified = "2026-08-01",
            },
        };

    public static Source OneSource => new()
    {
        Id = "s1",
        Type = SourceType.Packaging,
        RetrievedAt = "2026-08-01",
    };

    /// <summary>A per-100g panel whose numbers are internally consistent.</summary>
    public static NutritionBlock Panel(
        double? fat = 20,
        double? saturatedFat = 10,
        double? transFat = null,
        double? carbohydrate = 68,
        double? sugars = 18,
        double? addedSugars = null,
        double? protein = 6,
        double? fibre = 3,
        double? energyKcal = 476,
        double? energyKj = null,
        double? sodiumMg = null,
        NutritionBasis basis = NutritionBasis.Per100g,
        ServingInfo? serving = null,
        IReadOnlyList<string>? notDeclared = null) => new()
        {
            Basis = basis,
            Serving = serving,
            Values = new NutritionValues
            {
                EnergyKcal = energyKcal,
                EnergyKj = energyKj,
                ProteinG = protein,
                CarbohydrateG = carbohydrate,
                SugarsG = sugars,
                AddedSugarsG = addedSugars,
                FatG = fat,
                SaturatedFatG = saturatedFat,
                TransFatG = transFat,
                FibreG = fibre,
                SodiumMg = sodiumMg,
            },
            NotDeclared = notDeclared,
        };

    /// <summary>Provenance covering a product that also declares nutrition.</summary>
    public static Dictionary<string, ProvenanceEntry> ProvenanceWithNutrition(
        Confidence confidence = Confidence.High) => new()
        {
            ["name"] = new() { Source = "s1", Confidence = confidence },
            ["nutrition"] = new() { Source = "s1", Confidence = confidence },
        };
}
