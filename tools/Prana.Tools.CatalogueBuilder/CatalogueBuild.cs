using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Prana.Core.Json;
using Prana.Core.Model;

namespace Prana.Tools.CatalogueBuilder;

public sealed class BuildOptions
{
    public required string RepositoryRoot { get; init; }

    public required string OutputDirectory { get; init; }

    public required int CatalogueVersion { get; init; }

    /// <summary>Date stamped into the catalogue. Injected so a build is reproducible.</summary>
    public required DateOnly BuiltOn { get; init; }

    /// <summary>
    /// When set, only the most complete products are included, up to this many. This is the
    /// starter catalogue bundled inside the app.
    /// </summary>
    public int StarterSize { get; init; }

    public required string MinimumAppVersion { get; init; }
}

public sealed record BuildResult(
    string DatabasePath,
    string CompressedPath,
    long DatabaseBytes,
    long CompressedBytes,
    string Sha256,
    int Products,
    int Incomplete,
    TimeSpan Elapsed);

/// <summary>
/// Turns the repository into the SQLite catalogue a phone installs.
/// </summary>
public sealed class CatalogueBuild(BuildOptions options)
{
    public async Task<BuildResult> ExecuteAsync(TextWriter log, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(options.OutputDirectory);

        var name = options.StarterSize > 0 ? "catalogue-starter" : "catalogue";
        var databasePath = Path.Combine(options.OutputDirectory, $"{name}.db");
        var compressedPath = databasePath + ".br";

        File.Delete(databasePath);
        File.Delete(compressedPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);

            // Fixed page size and no journal keep the file deterministic. Anything that varies
            // between runs would defeat the reproducibility check.
            Pragma(connection, "PRAGMA page_size = 4096");
            Pragma(connection, "PRAGMA journal_mode = OFF");
            Pragma(connection, "PRAGMA synchronous = OFF");
            Pragma(connection, $"PRAGMA application_id = {CatalogueSchema.ApplicationId}");
            Pragma(connection, $"PRAGMA user_version = {CatalogueSchema.Version}");

            foreach (var statement in CatalogueSchema.Statements)
            {
                Pragma(connection, statement);
            }

            var counts = WriteEverything(connection, log, cancellationToken);

            log.WriteLine("  compacting");
            Pragma(connection, "VACUUM");

            await connection.CloseAsync();
            SqliteConnection.ClearAllPools();

            stopwatch.Stop();

            var databaseBytes = new FileInfo(databasePath).Length;

            log.WriteLine("  compressing");
            var compressedBytes = Compress(databasePath, compressedPath);

            return new BuildResult(
                databasePath,
                compressedPath,
                databaseBytes,
                compressedBytes,
                HashOf(compressedPath),
                counts.Products,
                counts.Incomplete,
                stopwatch.Elapsed);
        }
    }

    private (int Products, int Incomplete) WriteEverything(
        SqliteConnection connection,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        using var writer = new CatalogueWriter(connection);
        var data = Path.Combine(options.RepositoryRoot, "data");

        // Reference data first, so a product can never reference a row that is not there yet.
        var brands = Read<BrandRecord>(data, "brands");
        var categories = Read<CategoryRecord>(data, "categories");
        var ingredients = Read<IngredientRecord>(data, "ingredients");
        var countries = Read<CountryRecord>(data, "countries");

        foreach (var brand in brands)
        {
            writer.WriteBrand(brand);
        }

        foreach (var category in categories)
        {
            writer.WriteCategory(category);
        }

        foreach (var ingredient in ingredients)
        {
            writer.WriteIngredientRecord(ingredient);
        }

        foreach (var country in countries)
        {
            writer.WriteCountry(country);
        }

        log.WriteLine(
            $"  reference data: {brands.Count} brands, {categories.Count} categories, "
            + $"{ingredients.Count} ingredients, {countries.Count} countries");

        foreach (var product in SelectProducts(data, log, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteProduct(product);

            if (writer.ProductCount % 5000 == 0)
            {
                log.WriteLine($"  {writer.ProductCount:N0} products");
            }
        }

        WriteMeta(writer);
        writer.Commit();

        return (writer.ProductCount, writer.IncompleteCount);
    }

    /// <summary>
    /// Which products go in.
    /// </summary>
    /// <remarks>
    /// A full build takes everything, in barcode order, because a deterministic order is part of
    /// producing an identical file twice.
    ///
    /// A starter build takes the most complete records instead. The app bundles it so the first
    /// launch is useful before anything has been downloaded, and a bundle full of name-only
    /// records would make that first impression worse than an empty one.
    /// </remarks>
    private IEnumerable<ProductRecord> SelectProducts(
        string data,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(data, "products"), "*.json", SearchOption.AllDirectories)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        if (options.StarterSize <= 0)
        {
            log.WriteLine($"  full build: {files.Count:N0} product files");

            foreach (var file in files)
            {
                yield return PranaJson.Deserialize<ProductRecord>(File.ReadAllText(file));
            }

            yield break;
        }

        log.WriteLine($"  starter build: choosing the {options.StarterSize:N0} most complete of {files.Count:N0}");

        var chosen = files
            .Select(f => PranaJson.Deserialize<ProductRecord>(File.ReadAllText(f)))
            .Select(p => (Product: p, Score: Completeness(p)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            // Barcode breaks ties, so two builds of the same data choose the same products.
            .ThenBy(x => x.Product.Gtin, StringComparer.Ordinal)
            .Take(options.StarterSize)
            .Select(x => x.Product)
            .OrderBy(p => p.Gtin, StringComparer.Ordinal);

        foreach (var product in chosen)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return product;
        }
    }

    /// <summary>
    /// How much a record actually tells someone. Used only to choose the starter set, never
    /// shown to a user and never stored.
    /// </summary>
    private static int Completeness(ProductRecord product)
    {
        var score = 0;

        if (product.Nutrition is { Count: > 0 } nutrition)
        {
            score += 4;

            // A panel with more declared nutrients is more useful than one with two.
            score += Math.Min(4, Prana.Core.Rules.NutritionConsistency
                .DeclaredFieldNames(nutrition[0].Values).Count / 3);
        }

        if (!string.IsNullOrEmpty(product.IngredientsRaw))
        {
            score += 3;
        }

        if (product.Brand is not null)
        {
            score += 1;
        }

        if (product.Category is not null)
        {
            score += 1;
        }

        if (product.Package is not null)
        {
            score += 1;
        }

        return score;
    }

    private void WriteMeta(CatalogueWriter writer)
    {
        writer.WriteMeta("catalogue_version", options.CatalogueVersion.ToString(CultureInfo.InvariantCulture));
        writer.WriteMeta("schema_version", CatalogueSchema.Version.ToString(CultureInfo.InvariantCulture));
        writer.WriteMeta("built_on", options.BuiltOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        writer.WriteMeta("minimum_app_version", options.MinimumAppVersion);
        writer.WriteMeta("kind", options.StarterSize > 0 ? "starter" : "full");
        writer.WriteMeta("licence", "ODbL-1.0 (database), DbCL-1.0 (contents)");
        writer.WriteMeta(
            "attribution",
            "Contains data from Open Food Facts (https://world.openfoodfacts.org), made available "
            + "under the Open Database License (ODbL) v1.0.");
    }

    private static List<T> Read<T>(string data, string directory)
    {
        var path = Path.Combine(data, directory);

        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(path, "*.json", SearchOption.AllDirectories)
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(f => PranaJson.Deserialize<T>(File.ReadAllText(f)))
            .ToList();
    }

    private static void Pragma(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Compresses with Brotli rather than zstd.
    /// </summary>
    /// <remarks>
    /// The original plan named zstd. Brotli is built into .NET on both the build machine and the
    /// phone, at comparable ratios, so choosing it removes a native dependency from a mobile app
    /// that has to run on low-end Android devices. That is worth more than the last few percent
    /// of compression.
    /// </remarks>
    private static long Compress(string source, string destination)
    {
        using (var input = File.OpenRead(source))
        using (var output = File.Create(destination))
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize))
        {
            input.CopyTo(brotli);
        }

        return new FileInfo(destination).Length;
    }

    private static string HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
