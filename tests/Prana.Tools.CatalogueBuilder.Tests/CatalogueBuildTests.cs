using Microsoft.Data.Sqlite;
using Prana.Core.Json;
using Prana.Core.Model;
using Xunit;

namespace Prana.Tools.CatalogueBuilder.Tests;

/// <summary>
/// The catalogue is a published artefact with a documented schema, so these tests check what a
/// third-party client would actually see rather than only that the build completed.
/// </summary>
public sealed class CatalogueBuildTests : IDisposable
{
    private readonly string _root;

    public CatalogueBuildTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "prana-catalogue-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        Write("data/countries/IN.json", new CountryRecord
        {
            SchemaVersion = 1,
            Code = "IN",
            Name = "India",
            DefaultNutritionBasis = NutritionBasis.Per100g,
            SodiumDeclaredAs = "sodium",
        });

        Write("data/brands/parle.json", new BrandRecord
        {
            SchemaVersion = 1,
            Id = "parle",
            Name = "Parle",
            Sources = [Source()],
            Provenance = Provenance(),
        });

        Write("data/categories/biscuits.json", new CategoryRecord
        {
            SchemaVersion = 1,
            Id = "biscuits",
            Name = "Biscuits",
            TypicalBasis = NutritionBasis.Per100g,
            SubstitutableWith = ["cookies"],
            RelevantNutrients = ["sugars_g", "sodium_mg"],
        });

        // A complete product, declared per 100 g.
        WriteProduct(Product("08901719134845", "8901719134845", "Parle-G Biscuit",
            nutrition:
            [
                new NutritionBlock
                {
                    Basis = NutritionBasis.Per100g,
                    Values = new NutritionValues { EnergyKcal = 454, SugarsG = 25.5, SodiumMg = 296 },
                    NotDeclared = ["trans_fat_g"],
                }
            ],
            ingredientsRaw: "Wheat flour, sugar, palm oil"));

        // A product whose label declared a serving, which must survive as a serving.
        WriteProduct(Product("08901111111116", "8901111111116", "Example Rusk",
            nutrition:
            [
                new NutritionBlock
                {
                    Basis = NutritionBasis.PerServing,
                    Serving = new ServingInfo
                    {
                        Description = "2 pieces (25 g)",
                        Quantity = new Quantity { Value = 25, Unit = Unit.Gram },
                    },
                    Values = new NutritionValues { EnergyKcal = 113, SodiumMg = 74 },
                }
            ]));

        // A record with a name and nothing else.
        WriteProduct(Product("08902222222227", "8902222222227", "Mystery Packet"));
    }

    private static Source Source() => new()
    {
        Id = "s1",
        Type = SourceType.OpenDatabase,
        RetrievedAt = "2026-08-27",
        Licence = "ODbL-1.0",
    };

    private static Dictionary<string, ProvenanceEntry> Provenance() => new(StringComparer.Ordinal)
    {
        ["name"] = new() { Source = "s1", Confidence = Confidence.Medium },
    };

    private static ProductRecord Product(
        string gtin,
        string printed,
        string name,
        IReadOnlyList<NutritionBlock>? nutrition = null,
        string? ingredientsRaw = null) => new()
        {
            SchemaVersion = 1,
            Gtin = gtin,
            BarcodePrinted = printed,
            BarcodeFormat = BarcodeFormat.Ean13,
            Name = name,
            Brand = "parle",
            Category = "biscuits",
            Countries = ["IN"],
            Nutrition = nutrition,
            IngredientsRaw = ingredientsRaw,
            Sources = [Source()],
            Provenance = Provenance(),
            Verification = new Verification
            {
                Status = VerificationStatus.Unverified,
                LastVerified = "2026-08-27",
            },
        };

    private void WriteProduct(ProductRecord product) =>
        Write($"data/products/{Prana.Core.Barcodes.Gtin.ShardFor(product.Gtin)}/{product.Gtin}.json", product);

    private void Write<T>(string relative, T record)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, PranaJson.Serialize(record));
    }

    private async Task<(BuildResult Result, string Output)> BuildAsync(int starterSize = 0)
    {
        var output = Path.Combine(_root, starterSize > 0 ? "out-starter" : "out");

        var result = await new CatalogueBuild(new BuildOptions
        {
            RepositoryRoot = _root,
            OutputDirectory = output,
            CatalogueVersion = 7,
            BuiltOn = new DateOnly(2026, 8, 27),
            StarterSize = starterSize,
            MinimumAppVersion = "1.0.0",
        }).ExecuteAsync(TextWriter.Null, CancellationToken.None);

        return (result, output);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());

        connection.Open();
        return connection;
    }

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, typeof(T));
    }

    [Fact]
    public async Task Every_product_reaches_the_catalogue()
    {
        var (result, _) = await BuildAsync();

        Assert.Equal(3, result.Products);
        Assert.Equal(1, result.Incomplete);

        using var db = Open(result.DatabasePath);
        Assert.Equal(3, Scalar<long>(db, "SELECT count(*) FROM product"));
        Assert.Equal(2, Scalar<long>(db, "SELECT count(*) FROM nutrition"));
    }

    [Fact]
    public async Task A_barcode_lookup_uses_the_primary_key()
    {
        var (result, _) = await BuildAsync();

        using var db = Open(result.DatabasePath);
        using var command = db.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT * FROM product WHERE gtin = '08901719134845'";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        // Scanning 26,000 rows on every scan would be the difference between an instant answer
        // and a visible pause on a low-end phone.
        Assert.Contains("PRIMARY KEY", reader.GetString(reader.FieldCount - 1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_declared_serving_is_still_a_serving_in_the_catalogue()
    {
        var (result, _) = await BuildAsync();

        using var db = Open(result.DatabasePath);
        using var command = db.CreateCommand();
        command.CommandText =
            "SELECT basis, serving_description, serving_value FROM nutrition WHERE gtin = '08901111111116'";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        // The whole reason for using the JSONL export. A per-serving panel that arrives in the
        // catalogue as per 100 g is a lie the app would repeat to every user.
        Assert.Equal("per_serving", reader.GetString(0));
        Assert.Equal("2 pieces (25 g)", reader.GetString(1));
        Assert.Equal(25d, reader.GetDouble(2));
    }

    [Fact]
    public async Task Incomplete_records_are_marked_rather_than_inferred()
    {
        var (result, _) = await BuildAsync();

        using var db = Open(result.DatabasePath);

        // ADR-0026 depends on the app being able to tell these apart. Storing it means one
        // indexed question rather than a check every caller has to remember to repeat.
        Assert.Equal(1, Scalar<long>(db, "SELECT count(*) FROM product WHERE is_complete = 0"));
        Assert.Equal(
            "08902222222227",
            Scalar<string>(db, "SELECT gtin FROM product WHERE is_complete = 0"));
    }

    [Fact]
    public async Task Nutrients_the_packet_does_not_declare_are_recorded_as_such()
    {
        var (result, _) = await BuildAsync();

        using var db = Open(result.DatabasePath);

        Assert.Equal(
            "trans_fat_g",
            Scalar<string>(db, "SELECT field FROM nutrition_not_declared WHERE gtin = '08901719134845'"));
    }

    [Fact]
    public async Task Search_finds_a_product_by_a_word_in_its_name()
    {
        var (result, _) = await BuildAsync();

        using var db = Open(result.DatabasePath);

        Assert.Equal(
            "08902222222227",
            Scalar<string>(db, "SELECT gtin FROM product_search WHERE product_search MATCH 'mystery'"));
    }

    [Fact]
    public async Task Search_finds_every_product_of_a_brand()
    {
        var (result, _) = await BuildAsync();

        using var db = Open(result.DatabasePath);

        // All three fixtures are Parle, so a brand term has to return all of them rather than
        // whichever one happens to sit first.
        Assert.Equal(3, Scalar<long>(db, "SELECT count(*) FROM product_search WHERE product_search MATCH 'parle'"));
    }

    [Fact]
    public async Task The_licence_travels_with_the_catalogue()
    {
        var (result, _) = await BuildAsync();

        using var db = Open(result.DatabasePath);

        // A share-alike licence follows the data. Someone who downloads only this file must be
        // able to find out what they may do with it.
        Assert.Contains("ODbL", Scalar<string>(db, "SELECT value FROM meta WHERE key = 'licence'"));
        Assert.Contains(
            "Open Food Facts",
            Scalar<string>(db, "SELECT value FROM meta WHERE key = 'attribution'"));
        Assert.Equal("7", Scalar<string>(db, "SELECT value FROM meta WHERE key = 'catalogue_version'"));
    }

    [Fact]
    public async Task The_starter_catalogue_takes_the_most_complete_products()
    {
        var (result, _) = await BuildAsync(starterSize: 2);

        Assert.Equal(2, result.Products);

        using var db = Open(result.DatabasePath);

        // A bundle full of name-only records would make the first launch worse than an empty one.
        Assert.Equal(0, Scalar<long>(db, "SELECT count(*) FROM product WHERE is_complete = 0"));
    }

    [Fact]
    public async Task The_same_input_produces_the_same_bytes()
    {
        var first = await BuildAsync();

        Directory.Delete(first.Output, recursive: true);

        var second = await BuildAsync();

        // Required by the project plan. Without it, nobody downstream can tell whether a new
        // catalogue actually contains anything new.
        Assert.Equal(first.Result.Sha256, second.Result.Sha256);
    }

    [Fact]
    public async Task Compression_is_worth_doing()
    {
        var (result, _) = await BuildAsync();

        Assert.True(
            result.CompressedBytes < result.DatabaseBytes,
            "The compressed catalogue should be smaller than the database.");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
