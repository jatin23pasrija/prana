using System.Text;
using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Tools.Importer.Sources.OpenFoodFacts;
using Xunit;

namespace Prana.Tools.Importer.Tests;

/// <summary>
/// Re-importing must not rewrite records whose product has not changed.
/// </summary>
/// <remarks>
/// This is the test that was missing. The importer claimed idempotency and had none: the
/// retrieval date is stamped on every record, so the first monthly re-import rewrote 26,578
/// files to move two date lines, added twenty megabytes to the repository, and produced a pull
/// request of 31,749 files that nobody could review.
///
/// The diff size was the visible symptom. The damage was that `last_verified` drives the
/// freshness thresholds in DATA_POLICY.md, so a record could never become stale: every import
/// reset its clock, and a product last touched upstream years ago would report itself current
/// forever.
/// </remarks>
public sealed class ImportIdempotencyTests : IDisposable
{
    private readonly string _root;

    public ImportIdempotencyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "prana-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    /// <summary>
    /// The repository's own schemas, pointed at in place rather than copied into each test's
    /// temporary tree. Loading a schema registers it globally under its <c>$id</c> and registering
    /// the same id twice throws, so every test in the process has to load from one path.
    /// </summary>
    private static string SchemaDirectory()
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

    private const string OneProduct = """
        {"code":"8901719134845","product_name":"Parle-G Biscuit","brands":"Parle",
         "countries_tags":["en:india"],"nutrition_data_per":"100g",
         "ingredients_text":"Wheat flour, sugar, palm oil",
         "nutriments":{"energy-kcal_100g":454,"proteins_100g":6.9,"sugars_100g":25.5}}
        """;

    private static string OneLine(string json) =>
        string.Join(' ', json.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    private async Task<ImportReport> ImportAsync(string jsonl, DateOnly retrievedAt)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonl);

        var run = new ImportRun(new ImportOptions
        {
            RepositoryRoot = _root,
            SchemaDirectory = SchemaDirectory(),
            Adapter = new OffJsonlAdapter(() => new MemoryStream(bytes), retrievedAt),
        });

        return await run.ExecuteAsync(TextWriter.Null, TestContext.Current.CancellationToken);
    }

    private string RecordPath => Path.Combine(
        _root, "data", "products", "890", "08901719134845.json");

    [Fact]
    public async Task A_reimport_on_a_later_date_does_not_touch_an_unchanged_product()
    {
        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 8, 27));

        var before = await File.ReadAllTextAsync(RecordPath, TestContext.Current.CancellationToken);

        // The same product, seen five days later. Nothing about it changed.
        var second = await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 9, 1));

        var after = await File.ReadAllTextAsync(RecordPath, TestContext.Current.CancellationToken);

        Assert.Equal(before, after);
        Assert.Equal(0, second.Written);
        Assert.Equal(1, second.Unchanged);
    }

    [Fact]
    public async Task The_verification_date_does_not_creep_forward_on_its_own()
    {
        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 8, 27));
        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 9, 1));
        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 12, 25));

        var record = PranaJson.Deserialize<ProductRecord>(
            await File.ReadAllTextAsync(RecordPath, TestContext.Current.CancellationToken));

        // Three imports over four months. The date has to stay at the first one, or the staleness
        // thresholds in DATA_POLICY.md can never fire and nothing is ever flagged for re-checking.
        Assert.Equal("2026-08-27", record.Verification.LastVerified);
        Assert.Equal("2026-08-27", record.Sources[0].RetrievedAt);
    }

    [Fact]
    public async Task A_product_that_really_changed_is_rewritten_and_redated()
    {
        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 8, 27));

        // The manufacturer reformulated: sugar went up.
        var changed = OneLine(OneProduct).Replace("\"sugars_100g\":25.5", "\"sugars_100g\":28.1");

        var second = await ImportAsync(changed, new DateOnly(2026, 9, 1));

        var record = PranaJson.Deserialize<ProductRecord>(
            await File.ReadAllTextAsync(RecordPath, TestContext.Current.CancellationToken));

        Assert.Equal(1, second.Written);
        Assert.Equal(0, second.Unchanged);
        Assert.Equal(28.1, record.Nutrition![0].Values.SugarsG);

        // A real change is genuinely newly seen, so here the date does move.
        Assert.Equal("2026-09-01", record.Verification.LastVerified);
    }

    [Fact]
    public async Task A_new_product_is_written_on_a_reimport()
    {
        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 8, 27));

        var extra = OneLine(OneProduct).Replace("8901719134845", "8901111111116");
        var second = await ImportAsync(OneLine(OneProduct) + "\n" + extra, new DateOnly(2026, 9, 1));

        // One new, one untouched. This is what a monthly import should look like.
        Assert.Equal(1, second.Written);
        Assert.Equal(1, second.Unchanged);
    }

    [Fact]
    public async Task Brand_records_are_left_alone_too()
    {
        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 8, 27));

        var brandPath = Path.Combine(_root, "data", "brands", "parle.json");
        var before = await File.ReadAllTextAsync(brandPath, TestContext.Current.CancellationToken);

        await ImportAsync(OneLine(OneProduct), new DateOnly(2026, 9, 1));

        // Five thousand brands rewritten to move one line each is the same problem in miniature.
        Assert.Equal(before, await File.ReadAllTextAsync(brandPath, TestContext.Current.CancellationToken));
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
}
