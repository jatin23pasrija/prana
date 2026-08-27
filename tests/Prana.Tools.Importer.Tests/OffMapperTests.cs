using Prana.Core.Model;
using Prana.Tools.Importer.Sources.OpenFoodFacts;
using Xunit;

namespace Prana.Tools.Importer.Tests;

/// <summary>
/// The mapping is where an import either respects the data rules or quietly launders a violation
/// of them, so it is tested field by field rather than only end to end.
/// </summary>
public sealed class OffMapperTests
{
    private static readonly string[] Columns =
    [
        "code", "product_name", "brands", "countries_tags", "categories_tags", "ingredients_text",
        "nutrition_data_per", "serving_size", "serving_quantity",
        "product_quantity", "product_quantity_unit",
        "energy-kcal_100g", "proteins_100g", "carbohydrates_100g", "sugars_100g", "fat_100g",
        "saturated-fat_100g", "trans-fat_100g", "fiber_100g", "sodium_100g",
        "energy-kcal_serving", "proteins_serving", "carbohydrates_serving", "sugars_serving",
        "fat_serving", "saturated-fat_serving", "fiber_serving", "sodium_serving",
    ];

    private static OffRow Row(params (string Column, string Value)[] values)
    {
        var index = OffRow.IndexHeader(Columns);
        var fields = new string[Columns.Length];

        foreach (var (column, value) in values)
        {
            fields[index[column]] = value;
        }

        return OffRow.FromCsv(index, fields);
    }

    private static OffMapper Mapper() => new(new DateOnly(2026, 8, 27), "ODbL-1.0 (database), DbCL-1.0 (contents)");

    private static OffRow GoodRow(params (string Column, string Value)[] overrides)
    {
        var baseline = new List<(string, string)>
        {
            ("code", "8901719134845"),
            ("product_name", "Parle-G Biscuit"),
            ("brands", "Parle"),
            ("countries_tags", "en:india"),
            ("categories_tags", "en:snacks,en:biscuits-and-crackers,en:biscuits"),
            ("ingredients_text", "REFINED WHEAT FLOUR (MAIDA) 68%, SUGAR, REFINED PALM OIL"),
            ("nutrition_data_per", "100g"),
            ("energy-kcal_100g", "454"),
            ("proteins_100g", "6.9"),
            ("carbohydrates_100g", "77.3"),
            ("sugars_100g", "25.5"),
            ("fat_100g", "13"),
            ("saturated-fat_100g", "6"),
            ("sodium_100g", "0.296"),
        };

        baseline.AddRange(overrides.Select(o => (o.Column, o.Value)));
        return Row([.. baseline]);
    }

    [Fact]
    public void A_complete_row_maps_to_a_record()
    {
        Assert.True(Mapper().TryMap(GoodRow(), out var product, out var reason));
        Assert.Null(reason);

        Assert.Equal("08901719134845", product!.Gtin);
        Assert.Equal("8901719134845", product.BarcodePrinted);
        Assert.Equal(BarcodeFormat.Ean13, product.BarcodeFormat);
        Assert.Equal("Parle-G Biscuit", product.Name);
        Assert.Equal("parle", product.Brand);
        Assert.Equal("biscuits", product.Category);
        Assert.Equal(["IN"], product.Countries);
    }

    [Fact]
    public void Sodium_is_converted_from_grams_to_milligrams()
    {
        Mapper().TryMap(GoodRow(), out var product, out _);

        // Open Food Facts stores 0.296 g. An unnoticed factor of a thousand here would be worse
        // than having no sodium data at all.
        Assert.Equal(296, product!.Nutrition![0].Values.SodiumMg);
    }

    [Fact]
    public void A_per_serving_panel_is_never_recorded_as_per_100g()
    {
        // This is the rule that matters most. Open Food Facts publishes _100g columns even for
        // products whose label only stated a per-serving panel, by dividing through by the
        // serving size. Importing those would launder a conversion the data policy forbids.
        var row = GoodRow(
            ("nutrition_data_per", "serving"),
            ("serving_size", "2 biscuits (25 g)"),
            ("serving_quantity", "25"),
            ("energy-kcal_serving", "113"),
            ("proteins_serving", "1.7"),
            ("carbohydrates_serving", "19.3"),
            ("sugars_serving", "6.4"),
            ("fat_serving", "3.3"),
            ("sodium_serving", "0.074"));

        Assert.True(Mapper().TryMap(row, out var product, out _));

        var block = product!.Nutrition![0];

        Assert.Equal(NutritionBasis.PerServing, block.Basis);
        Assert.Equal("2 biscuits (25 g)", block.Serving!.Description);
        Assert.Equal(25, block.Serving.Quantity!.Value);

        // The per-serving figures, not the normalised per-100g ones sitting in the same row.
        Assert.Equal(113, block.Values.EnergyKcal);
        Assert.Equal(74, block.Values.SodiumMg);
    }

    [Fact]
    public void A_per_serving_panel_with_no_serving_described_is_not_recorded()
    {
        var row = GoodRow(("nutrition_data_per", "serving"), ("serving_size", ""));

        Mapper().TryMap(row, out var product, out _);

        // The ingredients still carry the record, but there is no honest way to say what the
        // numbers are per, so no panel is written.
        Assert.Null(product!.Nutrition);
    }

    [Fact]
    public void A_hundred_millilitre_basis_is_kept_as_millilitres()
    {
        Mapper().TryMap(GoodRow(("nutrition_data_per", "100ml")), out var product, out _);

        Assert.Equal(NutritionBasis.Per100ml, product!.Nutrition![0].Basis);
    }

    [Fact]
    public void An_indian_barcode_counts_even_with_no_country_tag()
    {
        // The country tag is contributor-entered and missing on many records. A 890 barcode was
        // issued by GS1 India, so taking either signal finds products the tag alone would lose.
        Assert.True(Mapper().TryMap(GoodRow(("countries_tags", "")), out var product, out _));
        Assert.NotNull(product);
    }

    [Fact]
    public void A_foreign_product_with_no_indian_signal_is_dropped()
    {
        var row = GoodRow(("code", "3017620422003"), ("countries_tags", "en:france"));

        Assert.False(Mapper().TryMap(row, out _, out var reason));
        Assert.Contains("India", reason);
    }

    [Theory]
    [InlineData("", "barcode")]
    [InlineData("8901719134846", "barcode")] // check digit changed
    public void An_unusable_barcode_is_dropped(string code, string expected)
    {
        Assert.False(Mapper().TryMap(GoodRow(("code", code)), out _, out var reason));
        Assert.Contains(expected, reason);
    }

    [Fact]
    public void A_row_with_no_name_is_dropped()
    {
        Assert.False(Mapper().TryMap(GoodRow(("product_name", "")), out _, out var reason));
        Assert.Contains("name", reason);
    }

    [Fact]
    public void A_row_with_nothing_but_a_name_is_still_kept()
    {
        var row = Row(
            ("code", "8901719134845"),
            ("product_name", "Mystery Packet"),
            ("countries_tags", "en:india"));

        // Telling someone the packet in their hand is Mystery Packet and that we know nothing
        // else beats telling them it does not exist. The record is honest about which of those
        // two things is true, and the app treats it as incomplete and still offers discovery.
        Assert.True(Mapper().TryMap(row, out var product, out _));

        Assert.NotNull(product);
        Assert.Null(product!.Nutrition);
        Assert.Null(product.IngredientsRaw);
        Assert.Equal(["name"], product.Provenance.Keys);
    }

    [Fact]
    public void Ingredients_alone_are_enough_to_keep_a_record()
    {
        var row = Row(
            ("code", "8901719134845"),
            ("product_name", "Mystery Packet"),
            ("countries_tags", "en:india"),
            ("ingredients_text", "Refined wheat flour, sugar, palm oil"));

        Assert.True(Mapper().TryMap(row, out var product, out _));
        Assert.Null(product!.Nutrition);
        Assert.NotNull(product.IngredientsRaw);
    }

    [Fact]
    public void Imported_records_are_never_marked_verified()
    {
        Mapper().TryMap(GoodRow(), out var product, out _);

        // Open Food Facts is community-entered. Marking it verified would make our own
        // verification vocabulary meaningless.
        Assert.Equal(VerificationStatus.Unverified, product!.Verification.Status);
        Assert.All(product.Provenance.Values, p => Assert.Equal(Confidence.Medium, p.Confidence));
    }

    [Fact]
    public void Provenance_only_claims_fields_that_are_present()
    {
        var row = Row(
            ("code", "8901719134845"),
            ("product_name", "Mystery Packet"),
            ("countries_tags", "en:india"),
            ("ingredients_text", "Refined wheat flour"));

        Mapper().TryMap(row, out var product, out _);

        Assert.Contains("name", product!.Provenance.Keys);
        Assert.Contains("ingredients_raw", product.Provenance.Keys);

        // A provenance entry for a field that does not exist backs nothing, and the validator
        // reports it as a stale path.
        Assert.DoesNotContain("nutrition", product.Provenance.Keys);
        Assert.DoesNotContain("brand", product.Provenance.Keys);
    }

    [Fact]
    public void Negative_and_absurd_numbers_are_discarded_rather_than_imported()
    {
        var row = GoodRow(("fat_100g", "-5"), ("proteins_100g", "not a number"));

        Mapper().TryMap(row, out var product, out _);

        Assert.Null(product!.Nutrition![0].Values.FatG);
        Assert.Null(product.Nutrition[0].Values.ProteinG);
    }

    [Theory]
    [InlineData("Parle", "parle")]
    [InlineData("Britannia Industries", "britannia-industries")]
    [InlineData("  Mother's  Recipe  ", "mother-s-recipe")]
    [InlineData("Nestlé", "nestle")]
    [InlineData("!!!", null)]
    [InlineData("", null)]
    public void Brand_names_become_slugs(string input, string? expected) =>
        Assert.Equal(expected, OffMapper.SlugOf(input));

    [Theory]
    [InlineData("en:snacks,en:biscuits-and-crackers,en:biscuits", "biscuits")]
    [InlineData("en:biscuits,en:cream-biscuits", "cream-biscuits")] // most specific wins
    [InlineData("en:sodas", "soft-drink")]
    [InlineData("en:something-we-do-not-know", null)]
    [InlineData("", null)]
    public void Category_tags_map_to_our_small_curated_set(string tags, string? expected) =>
        Assert.Equal(expected, CategoryMap.Map(tags));

    [Fact]
    public void Every_category_the_mapping_can_produce_has_a_record()
    {
        // A mapping that produces a category with no record would put a warning on every product
        // using it, which is how contributors learn to ignore warnings.
        var directory = Path.Combine(RepositoryRoot(), "data", "categories");

        foreach (var category in CategoryMap.KnownCategories)
        {
            Assert.True(
                File.Exists(Path.Combine(directory, $"{category}.json")),
                $"CategoryMap can produce '{category}' but data/categories/{category}.json does not exist.");
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Prana.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
