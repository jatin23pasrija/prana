using System.IO.Compression;
using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Data;
using Prana.Tools.CatalogueBuilder;
using Xunit;

namespace Prana.Data.Tests;

/// <summary>
/// Exercises the data layer against a catalogue produced by the real builder.
/// </summary>
/// <remarks>
/// A hand-written fixture database would test the repositories against a shape nobody ships. By
/// running the actual builder, these tests fail if the builder and the reader ever disagree,
/// which is the failure that would otherwise surface as an empty screen on a phone.
/// </remarks>
public sealed class DataLayerTests : IDisposable
{
    private readonly string _root;
    private readonly string _repo;
    private readonly TestStorage _storage;
    private readonly CataloguePaths _paths;
    private readonly CatalogueConnection _catalogue;

    public DataLayerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "prana-data-tests", Guid.NewGuid().ToString("N"));
        _repo = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(_root, "app"));

        BuildSourceRecords();

        _storage = new TestStorage(Path.Combine(_root, "app"));
        _paths = new CataloguePaths(_storage);
        _catalogue = new CatalogueConnection(_paths);
    }

    private sealed class TestStorage(string directory) : ICatalogueStorage
    {
        public string DataDirectory { get; } = directory;

        public string? BundlePath { get; set; }

        public Task<Stream?> OpenBundledCatalogueAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(
                BundlePath is not null && File.Exists(BundlePath) ? File.OpenRead(BundlePath) : null);
    }

    private void BuildSourceRecords()
    {
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
            Sources = [TestSource()],
            Provenance = TestProvenance(),
        });

        Write("data/categories/biscuits.json", new CategoryRecord
        {
            SchemaVersion = 1,
            Id = "biscuits",
            Name = "Biscuits",
            TypicalBasis = NutritionBasis.Per100g,
        });

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

        WriteProduct(Product("08902222222227", "8902222222227", "Mystery Namkeen"));
    }

    private static Source TestSource() => new()
    {
        Id = "s1",
        Type = SourceType.OpenDatabase,
        RetrievedAt = "2026-09-01",
        Licence = "ODbL-1.0",
    };

    private static Dictionary<string, ProvenanceEntry> TestProvenance() => new(StringComparer.Ordinal)
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
            Sources = [TestSource()],
            Provenance = TestProvenance(),
            Verification = new Verification
            {
                Status = VerificationStatus.Unverified,
                LastVerified = "2026-09-01",
            },
        };

    private void WriteProduct(ProductRecord product) =>
        Write($"data/products/{Core.Barcodes.Gtin.ShardFor(product.Gtin)}/{product.Gtin}.json", product);

    private void Write<T>(string relative, T record)
    {
        var path = Path.Combine(_repo, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, PranaJson.Serialize(record));
    }

    /// <summary>Builds a catalogue and installs it, the way the app does on first run.</summary>
    private async Task<InstallOutcome> InstallAsync()
    {
        var output = Path.Combine(_root, "build");

        var result = await new CatalogueBuild(new BuildOptions
        {
            RepositoryRoot = _repo,
            OutputDirectory = output,
            CatalogueVersion = 42,
            BuiltOn = new DateOnly(2026, 9, 1),
            StarterSize = 0,
            MinimumAppVersion = "1.0.0",
        }).ExecuteAsync(TextWriter.Null, CancellationToken.None);

        _storage.BundlePath = result.CompressedPath;

        return await new CatalogueInstaller(_storage, _paths, _catalogue)
            .EnsureInstalledAsync(CancellationToken.None);
    }

    // ---------------------------------------------------------------- installation

    [Fact]
    public async Task The_bundled_catalogue_is_unpacked_on_first_run()
    {
        Assert.Equal(InstallOutcome.InstalledFromBundle, await InstallAsync());

        var status = _catalogue.ReadStatus();

        Assert.True(status.IsInstalled);
        Assert.Equal(42, status.Version);
        Assert.Equal(3, status.ProductCount);
        Assert.Contains("Open Food Facts", status.Attribution);
    }

    [Fact]
    public async Task A_second_run_does_not_unpack_it_again()
    {
        await InstallAsync();

        var installer = new CatalogueInstaller(_storage, _paths, _catalogue);

        Assert.Equal(
            InstallOutcome.AlreadyInstalled,
            await installer.EnsureInstalledAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_build_with_no_bundled_catalogue_is_not_an_error()
    {
        // ADR-0030 has a flavour that ships no catalogue and downloads one instead. Starting
        // without a catalogue has to be an ordinary state, not a failure.
        var outcome = await new CatalogueInstaller(_storage, _paths, _catalogue)
            .EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(InstallOutcome.NoBundle, outcome);
        Assert.False(_catalogue.ReadStatus().IsInstalled);
    }

    [Fact]
    public async Task A_corrupt_bundle_leaves_the_app_working_without_a_catalogue()
    {
        var bundle = Path.Combine(_root, "broken.db.br");

        await using (var file = File.Create(bundle))
        await using (var brotli = new BrotliStream(file, CompressionMode.Compress))
        {
            // Valid Brotli, not a database. This is what a truncated or corrupted download
            // looks like once it has been decompressed.
            brotli.Write("this is not a database"u8);
        }

        _storage.BundlePath = bundle;

        var outcome = await new CatalogueInstaller(_storage, _paths, _catalogue)
            .EnsureInstalledAsync(CancellationToken.None);

        Assert.Equal(InstallOutcome.BundleUnusable, outcome);
        Assert.False(_catalogue.ReadStatus().IsInstalled);

        // Nothing half-written survives. A file that opens but is not a catalogue would be worse
        // than none, because everything downstream would treat it as real.
        Assert.False(File.Exists(_paths.Catalogue));
        Assert.False(File.Exists(_paths.Staging));
    }

    [Fact]
    public void A_missing_catalogue_reports_cleanly_rather_than_throwing()
    {
        // This runs during startup. An exception here is a boot loop.
        var status = _catalogue.ReadStatus();

        Assert.False(status.IsInstalled);
        Assert.Equal(0, status.ProductCount);
    }

    [Fact]
    public void A_file_that_is_not_a_database_reports_cleanly()
    {
        Directory.CreateDirectory(_storage.DataDirectory);
        File.WriteAllText(_paths.Catalogue, "not a database at all");

        Assert.False(_catalogue.ReadStatus().IsInstalled);
    }

    // ---------------------------------------------------------------- lookup

    [Fact]
    public async Task A_barcode_finds_its_product_offline()
    {
        await InstallAsync();

        var product = await new ProductRepository(_catalogue)
            .FindAsync("8901719134845", CancellationToken.None);

        Assert.NotNull(product);
        Assert.Equal("Parle-G Biscuit", product!.Name);
        Assert.Equal("parle", product.Brand);
        Assert.Equal(454, product.Nutrition![0].Values.EnergyKcal);
        Assert.Equal(296, product.Nutrition[0].Values.SodiumMg);
    }

    [Fact]
    public async Task A_barcode_is_normalised_before_it_is_looked_up()
    {
        await InstallAsync();

        var repository = new ProductRepository(_catalogue);

        // The same packet, entered as printed and as the canonical key. Both must find it, or a
        // scan of a UPC-A packet would miss a record stored under its EAN-13 key.
        var printed = await repository.FindAsync("8901719134845", CancellationToken.None);
        var canonical = await repository.FindAsync("08901719134845", CancellationToken.None);

        Assert.NotNull(printed);
        Assert.Equal(printed!.Gtin, canonical!.Gtin);
    }

    [Fact]
    public async Task An_unknown_barcode_returns_nothing_rather_than_failing()
    {
        await InstallAsync();

        // This is the case that starts online discovery, so it has to be an ordinary null.
        var product = await new ProductRepository(_catalogue)
            .FindAsync("8909999999992", CancellationToken.None);

        Assert.Null(product);
    }

    [Fact]
    public async Task A_declared_serving_survives_into_the_app()
    {
        await InstallAsync();

        var product = await new ProductRepository(_catalogue)
            .FindAsync("8901111111116", CancellationToken.None);

        var block = product!.Nutrition![0];

        Assert.Equal(NutritionBasis.PerServing, block.Basis);
        Assert.Equal("2 pieces (25 g)", block.Serving!.Description);
        Assert.Equal(25, block.Serving.Quantity!.Value);
    }

    [Fact]
    public async Task Nutrients_the_packet_does_not_declare_stay_distinguishable()
    {
        await InstallAsync();

        var product = await new ProductRepository(_catalogue)
            .FindAsync("8901719134845", CancellationToken.None);

        // Losing this in the data layer would make the app show the same thing for "the packet
        // does not state it" and "nobody has looked", which is the distinction the whole unknown
        // model exists to preserve.
        Assert.Equal(["trans_fat_g"], product!.Nutrition![0].NotDeclared);
        Assert.Null(product.Nutrition[0].Values.TransFatG);
    }

    [Fact]
    public async Task Provenance_survives_into_the_app()
    {
        await InstallAsync();

        var product = await new ProductRepository(_catalogue)
            .FindAsync("8901719134845", CancellationToken.None);

        // If a screen can show a number, it has to be able to say where it came from.
        Assert.NotEmpty(product!.Sources);
        Assert.NotEmpty(product.Provenance);
        Assert.Equal(SourceType.OpenDatabase, product.Sources[0].Type);
    }

    // ---------------------------------------------------------------- search

    [Fact]
    public async Task Search_finds_a_product_by_a_word_in_its_name()
    {
        await InstallAsync();

        var results = await new SearchRepository(_catalogue)
            .SearchAsync("namkeen", 20, CancellationToken.None);

        Assert.Equal("08902222222227", Assert.Single(results).Gtin);
    }

    [Fact]
    public async Task Search_matches_a_prefix_so_it_works_while_typing()
    {
        await InstallAsync();

        var results = await new SearchRepository(_catalogue)
            .SearchAsync("mys", 20, CancellationToken.None);

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_reports_which_results_are_incomplete()
    {
        await InstallAsync();

        var results = await new SearchRepository(_catalogue)
            .SearchAsync("namkeen", 20, CancellationToken.None);

        // The app has to offer discovery for these, so the flag has to reach it.
        Assert.False(Assert.Single(results).IsComplete);
    }

    [Theory]
    [InlineData("parle's")]
    [InlineData("parle AND")]
    [InlineData("\"unclosed")]
    [InlineData("*")]
    [InlineData("()")]
    public async Task Search_survives_whatever_someone_types(string query)
    {
        await InstallAsync();

        // FTS5 has its own syntax. Passing raw input through would turn an apostrophe or the
        // word AND into a syntax error in front of a shopper.
        var results = await new SearchRepository(_catalogue)
            .SearchAsync(query, 20, CancellationToken.None);

        Assert.NotNull(results);
    }

    [Fact]
    public async Task Searching_with_no_catalogue_returns_nothing_rather_than_failing()
    {
        var results = await new SearchRepository(_catalogue)
            .SearchAsync("parle", 20, CancellationToken.None);

        Assert.Empty(results);
    }

    // ---------------------------------------------------------------- user database

    [Fact]
    public void Migrations_run_once_and_are_safe_to_repeat()
    {
        var user = new UserDatabase(_paths);

        var first = user.Migrate();
        var second = user.Migrate();

        Assert.True(first > 0);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Scan_history_records_misses_as_well_as_hits()
    {
        var user = new UserDatabase(_paths);
        user.Migrate();

        user.RecordScan("08901719134845", found: true, DateTimeOffset.UtcNow);
        user.RecordScan("08909999999992", found: false, DateTimeOffset.UtcNow.AddSeconds(1));

        var recent = user.RecentScans(10);

        // The misses are the demand signal the catalogue grows by, so they are as important to
        // keep as the hits.
        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, s => !s.Found);
    }

    [Fact]
    public async Task The_installed_catalogue_is_recorded_for_the_app_to_display()
    {
        await InstallAsync();

        var user = new UserDatabase(_paths);
        user.Migrate();
        user.RecordInstalledCatalogue(_catalogue.ReadStatus(), DateTimeOffset.UtcNow);

        // Recording it twice must update rather than fail, because it happens on every install.
        user.RecordInstalledCatalogue(_catalogue.ReadStatus(), DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task The_user_database_survives_the_catalogue_being_deleted()
    {
        await InstallAsync();

        var user = new UserDatabase(_paths);
        user.Migrate();
        user.RecordScan("08901719134845", found: true, DateTimeOffset.UtcNow);

        // Sync replaces the catalogue wholesale. ADR-0007 exists so that costs a download and
        // nothing a person created.
        File.Delete(_paths.Catalogue);

        Assert.Single(user.RecentScans(10));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

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
