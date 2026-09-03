using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Core.Rules;
using Xunit;

namespace Prana.Core.Tests;

/// <summary>
/// Palm detection runs against the real dictionary in data/ingredients, not a fixture, because
/// the dictionary is the feature. A test that invented its own aliases would pass while the
/// shipped vocabulary was wrong.
/// </summary>
public sealed class PalmDetectionTests
{
    private static readonly PalmDetection Detector = new(LoadDictionary());

    private static IReadOnlyList<IngredientRecord> LoadDictionary()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Prana.sln")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory!.FullName, "data", "ingredients");

        return [.. Directory.EnumerateFiles(path, "*.json")
            .Select(f => PranaJson.Deserialize<IngredientRecord>(File.ReadAllText(f)))];
    }

    [Theory]
    // Wordings taken from real imported products, with the number of products carrying each.
    [InlineData("Refined palm oil, sugar, salt")]                       // 196 products
    [InlineData("Wheat flour, sugar, palm oil, salt")]                  // 182
    [InlineData("Refined palmolein oil, spices")]                       // 110
    [InlineData("Sugar, refined palmolein, milk solids")]               // 28
    [InlineData("Maida, hydrogenated palm oil")]                        // 14
    [InlineData("Cocoa butter, palm kernel oil")]                       // 12
    [InlineData("Flour, refined palm & palmolein oil")]                 // 10
    [InlineData("Sugar, edible palm oil")]                              // 8
    // A misspelling that a naive matcher misses entirely. A missed match reads as absence.
    [InlineData("Refined palmolien oil, salt")]                         // 3
    [InlineData("Huile: oleine d'huile de palme raffinee")]             // 2, with accents
    public void Palm_on_the_label_is_reported_as_present(string ingredients)
    {
        var finding = Detector.Detect(ingredients);

        Assert.Equal(PalmState.Present, finding.State);
        Assert.NotEmpty(finding.Definite);
    }

    [Theory]
    // The commonest Indian construction: a generic oil, then the real one in brackets. 585
    // products in the catalogue do this and every one of them read as "palm not stated" until
    // the bare word became an alias. Found on a device, on a real Britannia biscuit.
    [InlineData("REFINED WHEAT FLOUR, EDIBLE VEGETABLE OIL (PALM), SUGAR")]     // 62 products
    [InlineData("Edible vegetable oil (palm oil), sugar")]                      // 62
    [InlineData("Edible vegetable oil (palmolein), wheat flour")]               // 71
    [InlineData("Vegetable fat (palm), sugar")]                                 // 24
    [InlineData("Vegetable oil (palm), salt")]                                  // 15
    [InlineData("Edible vegetable oil (refined palmolein oil)")]                // 17
    public void An_oil_named_in_brackets_is_read_as_present_not_as_unstated(string ingredients)
    {
        var finding = Detector.Detect(ingredients);

        Assert.Equal(PalmState.Present, finding.State);
        Assert.NotEmpty(finding.Definite);
    }

    [Fact]
    public void The_bare_word_palm_cannot_claim_text_a_longer_entry_owns()
    {
        // The bare alias is only safe because longer entries claim their own text first. If that
        // ever regresses, these three become false positives rather than a silent miss.
        Assert.Equal(PalmState.NotDetected, Detector.Detect("Rice flour, palm sugar").State);
        Assert.Equal(PalmState.NotDetected, Detector.Detect("Wheat, palm jaggery, ghee").State);
        Assert.NotEqual(PalmState.Present, Detector.Detect("Milk solids, vitamin a palmitate").State);
    }

    [Fact]
    public void A_declared_percentage_is_reported_and_never_invented()
    {
        var withPercentage = Detector.Detect("Wheat flour, palm oil (25%), sugar");
        var without = Detector.Detect("Wheat flour, palm oil, sugar");

        Assert.Equal(PalmState.ConfirmedQuantity, withPercentage.State);
        Assert.Equal(25, withPercentage.Definite[0].Percentage);
        Assert.Contains("25%", withPercentage.Statement);

        // The same product without a printed number gets no number. Position in the list is not
        // evidence of quantity, and estimating from it would be inventing data.
        Assert.Equal(PalmState.Present, without.State);
        Assert.Null(without.Definite[0].Percentage);
        Assert.Contains("does not say how much", without.Statement);
    }

    [Theory]
    // The commonest fat wording in the catalogue: 635 products say this and name no oil.
    [InlineData("Refined wheat flour, sugar, edible vegetable oil, salt")]
    [InlineData("Maida, vanaspati, sugar")]
    [InlineData("Flour, interesterified vegetable fat")]
    [InlineData("Sugar, bakery shortening, salt")]
    [InlineData("Wheat flour, hydrogenated vegetable fat")]
    public void An_unnamed_oil_is_unknown_rather_than_absent(string ingredients)
    {
        // This is the case a text search for "palm" gets wrong. It finds nothing and the app
        // would report "no palm detected", which is a confident answer with nothing behind it.
        var finding = Detector.Detect(ingredients);

        Assert.Equal(PalmState.Unknown, finding.State);
        Assert.Empty(finding.Definite);
        Assert.NotEmpty(finding.Possible);
    }

    [Fact]
    public void Vitamin_a_palmitate_is_not_reported_as_containing_palm_oil()
    {
        // Contains the letters "palm" and is a trace vitamin, not a fat. A text search calls
        // this Present.
        var finding = Detector.Detect("Milk solids, sugar, vitamin a palmitate, vitamin d");

        Assert.NotEqual(PalmState.Present, finding.State);
        Assert.Empty(finding.Definite);

        // And the sentence must not tell someone their milk powder might contain palm oil.
        Assert.DoesNotContain("often palm", finding.Statement);
    }

    [Fact]
    public void Palm_sugar_is_not_palm_oil()
    {
        // Boiled palm sap. Different part of a different plant, and nothing to do with palm oil.
        var finding = Detector.Detect("Rice flour, palm sugar, cardamom");

        Assert.Equal(PalmState.NotDetected, finding.State);
    }

    [Fact]
    public void Palm_kernel_oil_is_not_reported_as_palm_oil()
    {
        // Different oil, much higher in saturated fat. Longest alias must win the text.
        var finding = Detector.Detect("Cocoa solids, palm kernel oil, sugar");

        Assert.Equal("palm-kernel-oil", finding.Definite[0].IngredientId);
        Assert.Single(finding.Definite);
    }

    [Fact]
    public void A_clean_label_says_the_list_was_read_not_that_the_product_is_clear()
    {
        var finding = Detector.Detect("Groundnut oil, salt, red chilli, turmeric");

        Assert.Equal(PalmState.NotDetected, finding.State);

        // The distinction matters: we read an ingredient list, we did not test the product.
        Assert.Contains("not a guarantee", finding.Statement);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_ingredients_is_its_own_answer(string? ingredients)
    {
        // Not the same as "no palm found", and the 15,414 incomplete records in this catalogue
        // make it the single most common case.
        var finding = Detector.Detect(ingredients);

        Assert.Equal(PalmState.NoIngredients, finding.State);
        Assert.Contains("no ingredient list", finding.Statement);
    }

    [Fact]
    public void A_word_is_not_matched_inside_a_longer_word()
    {
        // Palmyra is a different tree and palmitic acid is a fatty acid. Neither is palm oil, and
        // both start with the same five letters.
        Assert.NotEqual(PalmState.Present, Detector.Detect("Palmyra fibre, water").State);
        Assert.NotEqual(PalmState.Present, Detector.Detect("Water, palmitic acid").State);
    }

    [Fact]
    public void A_separator_between_two_names_is_not_flattened_away()
    {
        // "(palm), sugar" flattens to "palm sugar", which is a different ingredient and claims
        // the text. The packet says palm; the app said not stated. Found on a device.
        var finding = Detector.Detect("Edible vegetable oil (palm), sugar, salt");

        Assert.Equal(PalmState.Present, finding.State);
        Assert.Equal("palm-oil", finding.Definite[0].IngredientId);
    }

    [Fact]
    public void A_declared_percentage_is_read_from_the_parsed_tree_when_there_is_one()
    {
        var parsed = new List<Ingredient>
        {
            new() { Raw = "Refined palm oil", Canonical = "palm-oil", Percentage = 18.5 },
        };

        var finding = Detector.Detect("Refined palm oil, sugar", parsed);

        Assert.Equal(PalmState.ConfirmedQuantity, finding.State);
        Assert.Equal(18.5, finding.Definite[0].Percentage);
    }
}
