using System.Diagnostics;
using System.Text.Json;
using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Tools.Validator.Checks;

namespace Prana.Tools.Validator;

/// <summary>What a validation run was asked to do.</summary>
public sealed class ValidationOptions
{
    public required string RepositoryRoot { get; init; }

    public required string SchemaDirectory { get; init; }

    /// <summary>Files or directories to check.</summary>
    public required IReadOnlyList<string> Paths { get; init; }

    /// <summary>Promotes warnings to errors. The import quality gate in F04 needs this.</summary>
    public bool Strict { get; init; }

    /// <summary>Injected so freshness rules are deterministic in tests.</summary>
    public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);
}

/// <summary>The outcome of a run.</summary>
public sealed record ValidationResult(
    IReadOnlyList<Diagnostic> Diagnostics,
    int FilesChecked,
    TimeSpan Elapsed)
{
    public int ErrorCount => Diagnostics.Count(d => d.Severity == Severity.Error);

    public int WarningCount => Diagnostics.Count(d => d.Severity == Severity.Warning);

    /// <summary>Whether this run should stop a merge.</summary>
    public bool Blocks(bool strict) => ErrorCount > 0 || (strict && WarningCount > 0);
}

/// <summary>
/// Runs every check over a set of files.
/// </summary>
/// <remarks>
/// Files are streamed one at a time and disposed immediately. Only the small facts needed for
/// cross-record rules are retained, so memory stays flat whether the tree holds fifty records or
/// fifty thousand.
/// </remarks>
public sealed class ValidationRun(ValidationOptions options)
{
    private readonly SchemaCheck _schema = new(options.SchemaDirectory);

    public ValidationResult Execute()
    {
        var stopwatch = Stopwatch.StartNew();
        var diagnostics = new List<Diagnostic>();
        var index = new RecordIndex();
        var filesChecked = 0;

        foreach (var path in EnumerateFiles(options.Paths))
        {
            filesChecked++;

            using var file = RecordFile.Read(path, options.RepositoryRoot);

            if (file.ReadFailure is { } failure)
            {
                diagnostics.Add(failure);
                continue;
            }

            diagnostics.AddRange(CheckFile(file, index));
        }

        diagnostics.AddRange(index.Check());
        stopwatch.Stop();

        var ordered = diagnostics
            .OrderBy(d => d.File, StringComparer.Ordinal)
            .ThenByDescending(d => d.Severity)
            .ThenBy(d => d.Line)
            .ToList();

        return new ValidationResult(ordered, filesChecked, stopwatch.Elapsed);
    }

    private IEnumerable<Diagnostic> CheckFile(RecordFile file, RecordIndex index)
    {
        if (file.Kind == RecordKind.Unknown)
        {
            yield break;
        }

        if (!CanonicalFormat.IsCanonical(file.Text, out _))
        {
            yield return file.At(
                Severity.Warning,
                Rules.NotCanonicalFormat,
                string.Empty,
                "This file is not in the canonical format. Run: dotnet run --project "
                    + "tools/Prana.Tools.Validator -- format <path>");
        }

        var schemaFailures = _schema.Check(file).ToList();

        if (schemaFailures.Count > 0)
        {
            // Nothing below can be trusted once the shape is wrong, and reporting arithmetic
            // errors about fields that failed schema validation is noise on top of a real problem.
            foreach (var failure in schemaFailures)
            {
                yield return failure;
            }

            yield break;
        }

        foreach (var diagnostic in CheckTyped(file, index))
        {
            yield return diagnostic;
        }
    }

    private IEnumerable<Diagnostic> CheckTyped(RecordFile file, RecordIndex index)
    {
        var diagnostics = new List<Diagnostic>();

        try
        {
            switch (file.Kind)
            {
                case RecordKind.Product:
                    CheckProduct(file, index, diagnostics);
                    break;

                case RecordKind.Ingredient:
                    CheckIngredient(file, index, diagnostics);
                    break;

                case RecordKind.Brand:
                    CheckBrand(file, index, diagnostics);
                    break;

                case RecordKind.Category:
                    index.AddCategory(PranaJson.Deserialize<CategoryRecord>(file.Text).Id);
                    break;

                case RecordKind.Country:
                    index.AddCountry(PranaJson.Deserialize<CountryRecord>(file.Text).Code);
                    break;

                case RecordKind.Alternative:
                    _ = PranaJson.Deserialize<AlternativeRecord>(file.Text);
                    break;
            }
        }
        catch (JsonException ex)
        {
            // The schema passed but the model refused it. That is a schema and model that have
            // drifted apart, which is worth saying plainly rather than crashing the run.
            diagnostics.Add(file.At(
                Severity.Error,
                Rules.UnreadableRecord,
                string.Empty,
                $"This record passed schema validation but could not be loaded: {ex.Message}"));
        }

        return diagnostics;
    }

    private static void CheckProduct(RecordFile file, RecordIndex index, List<Diagnostic> diagnostics)
    {
        var product = PranaJson.Deserialize<ProductRecord>(file.Text);

        index.AddProduct(file, product.Gtin);

        diagnostics.AddRange(ProductChecks.Check(file, product, DateOnly.FromDateTime(DateTime.UtcNow)));

        diagnostics.AddRange(ProvenanceChecks.Check(
            file,
            product.Provenance,
            product.Sources,
            ProvenanceChecks.RequiredPointersFor(file, product),
            product.Verification,
            product.Conflicts));

        if (product.Brand is { Length: > 0 } brand)
        {
            index.AddReference(file, RecordIndex.ReferenceKind.Brand, "/brand", brand);
        }

        if (product.Category is { Length: > 0 } category)
        {
            index.AddReference(file, RecordIndex.ReferenceKind.Category, "/category", category);
        }

        for (var i = 0; i < product.Countries.Count; i++)
        {
            index.AddReference(file, RecordIndex.ReferenceKind.Country, $"/countries/{i}", product.Countries[i]);
        }

        if (product.Ingredients is not null)
        {
            AddIngredientReferences(file, index, product.Ingredients, "/ingredients");
        }
    }

    private static void AddIngredientReferences(
        RecordFile file,
        RecordIndex index,
        IReadOnlyList<Ingredient> ingredients,
        string pointer)
    {
        for (var i = 0; i < ingredients.Count; i++)
        {
            var ingredient = ingredients[i];

            if (ingredient.Canonical is { Length: > 0 } canonical)
            {
                index.AddReference(file, RecordIndex.ReferenceKind.Ingredient, $"{pointer}/{i}/canonical", canonical);
            }

            if (ingredient.Children is { Count: > 0 } children)
            {
                AddIngredientReferences(file, index, children, $"{pointer}/{i}/children");
            }
        }
    }

    private static void CheckIngredient(RecordFile file, RecordIndex index, List<Diagnostic> diagnostics)
    {
        var ingredient = PranaJson.Deserialize<IngredientRecord>(file.Text);
        index.AddIngredient(ingredient.Id);

        diagnostics.AddRange(ProvenanceChecks.Check(
            file,
            ingredient.Provenance,
            ingredient.Sources,
            ProvenanceChecks.RequiredPointersFor(file, "/aliases", "/flags", "/explanation"),
            verification: null,
            conflicts: null));
    }

    private static void CheckBrand(RecordFile file, RecordIndex index, List<Diagnostic> diagnostics)
    {
        var brand = PranaJson.Deserialize<BrandRecord>(file.Text);
        index.AddBrand(brand.Id);

        diagnostics.AddRange(ProvenanceChecks.Check(
            file,
            brand.Provenance,
            brand.Sources,
            ProvenanceChecks.RequiredPointersFor(file, "/name", "/owner"),
            verification: null,
            conflicts: null));
    }

    /// <summary>
    /// Expands the requested paths into record files, in a stable order so two runs over the
    /// same tree report findings in the same sequence.
    /// </summary>
    public static IEnumerable<string> EnumerateFiles(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                yield return Path.GetFullPath(path);
                continue;
            }

            if (!Directory.Exists(path))
            {
                continue;
            }

            var found = Directory
                .EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .OrderBy(p => p, StringComparer.Ordinal);

            foreach (var file in found)
            {
                yield return file;
            }
        }
    }
}
