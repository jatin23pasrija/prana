using System.IO.Compression;
using System.Text;
using Prana.Core.Model;
using Prana.Tools.Importer.Sources.OpenFoodFacts;
using Xunit;

namespace Prana.Tools.Importer.Tests;

/// <summary>
/// The JSONL export is the only import path, so its reader is tested against the document shape
/// the export actually publishes, nested nutriments and tag arrays included.
/// </summary>
public sealed class OffJsonlAdapterTests
{
    private const string ParleG = """
        {"code":"8901719134845","product_name":"Parle-G Biscuit","brands":"Parle",
         "countries_tags":["en:india"],"categories_tags":["en:snacks","en:biscuits"],
         "ingredients_text":"REFINED WHEAT FLOUR (MAIDA) 68%, SUGAR, REFINED PALM OIL",
         "nutrition_data_per":"100g","product_quantity":45,"product_quantity_unit":"g",
         "nutriments":{"energy-kcal_100g":454,"proteins_100g":6.9,"carbohydrates_100g":77.3,
                       "sugars_100g":25.5,"fat_100g":13,"saturated-fat_100g":6,"sodium_100g":0.296}}
        """;

    private static async Task<List<ImportCandidate>> ReadAsync(string jsonl, bool gzip = false)
    {
        var bytes = Encoding.UTF8.GetBytes(jsonl);

        if (gzip)
        {
            using var buffer = new MemoryStream();

            using (var compressor = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                compressor.Write(bytes);
            }

            bytes = buffer.ToArray();
        }

        var adapter = new OffJsonlAdapter(() => new MemoryStream(bytes), new DateOnly(2026, 8, 27));
        var results = new List<ImportCandidate>();

        await foreach (var candidate in adapter.ReadAsync(CancellationToken.None))
        {
            results.Add(candidate);
        }

        return results;
    }

    private static string OneLine(string json) =>
        string.Join(' ', json.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()));

    [Fact]
    public async Task Nutrition_is_read_out_of_the_nested_nutriments_object()
    {
        var results = await ReadAsync(OneLine(ParleG));

        var product = Assert.Single(results).Product;

        Assert.NotNull(product);
        Assert.Equal("08901719134845", product!.Gtin);
        Assert.Equal(454, product.Nutrition![0].Values.EnergyKcal);
        Assert.Equal(296, product.Nutrition[0].Values.SodiumMg);
    }

    [Fact]
    public async Task Tag_arrays_are_matched_the_same_way_as_the_flat_export()
    {
        var results = await ReadAsync(OneLine(ParleG));

        // countries_tags and categories_tags are arrays here and comma-joined strings in the
        // tab separated export. Both have to behave identically or the filter silently changes.
        Assert.Equal("biscuits", results[0].Product!.Category);
        Assert.Equal(["IN"], results[0].Product!.Countries);
    }

    [Fact]
    public async Task A_gzipped_stream_is_detected_and_decompressed()
    {
        // The export arrives compressed, but a pipeline may have decompressed it already, and a
        // caller who has to remember which will eventually get it wrong.
        var results = await ReadAsync(OneLine(ParleG), gzip: true);

        Assert.NotNull(Assert.Single(results).Product);
    }

    [Fact]
    public async Task A_product_that_does_not_say_what_its_numbers_are_per_gets_no_nutrition()
    {
        // This is the reason the JSONL export is used at all. The tab separated export has no
        // nutrition_data_per field, so every product would land here. Defaulting to per 100 g
        // would be the silent conversion the data policy forbids, applied catalogue-wide.
        var withoutBasis = OneLine(ParleG).Replace("\"nutrition_data_per\":\"100g\",", string.Empty);

        var product = (await ReadAsync(withoutBasis)).Single().Product;

        Assert.NotNull(product);
        Assert.Null(product!.Nutrition);
        Assert.DoesNotContain("nutrition", product.Provenance.Keys);
    }

    [Fact]
    public async Task A_malformed_line_is_dropped_without_ending_the_import()
    {
        var jsonl = OneLine(ParleG) + "\n{ this is not json\n" + OneLine(ParleG).Replace("8901719134845", "8901111111116");

        var results = await ReadAsync(jsonl);

        Assert.Equal(3, results.Count);
        Assert.Equal(2, results.Count(r => r.Product is not null));
        Assert.Contains(results, r => r.DropReason?.Contains("not valid JSON") == true);
    }

    [Fact]
    public async Task Every_record_carries_the_source_licence()
    {
        var results = await ReadAsync(OneLine(ParleG));
        var source = results[0].Product!.Sources.Single();

        Assert.Equal(SourceType.OpenDatabase, source.Type);
        Assert.Contains("ODbL", source.Licence);
        Assert.Contains("DbCL", source.Licence);
        Assert.Equal("2026-08-27", source.RetrievedAt);
    }
}
