using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Core.Rules;
using Xunit;

namespace Prana.Core.Tests;

/// <summary>
/// The indicator engine, run against the rule files that ship, so a wrong threshold in the data
/// fails here rather than on someone's phone.
/// </summary>
public sealed class IndicatorEngineTests
{
    private static readonly IReadOnlyList<RuleSet> Rules = LoadRules();

    private static IReadOnlyList<RuleSet> LoadRules()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Prana.sln")))
        {
            directory = directory.Parent;
        }

        return [.. Directory
            .EnumerateFiles(Path.Combine(directory!.FullName, "rules"), "*.json", SearchOption.AllDirectories)
            .Select(f => PranaJson.Deserialize<RuleSet>(File.ReadAllText(f)))];
    }

    private static NutritionBlock Food(NutritionValues values, ServingInfo? serving = null) =>
        new() { Basis = NutritionBasis.Per100g, Values = values, Serving = serving };

    private static Indicator Single(NutritionBlock block, string nutrient) =>
        Assert.Single(IndicatorEngine.Evaluate(block, Rules), i => i.Nutrient == nutrient);

    [Theory]
    // The published boundaries, tested on the boundary itself. "At or below 5.0 is Lower" and
    // "above 22.5 is Higher" are exact wordings, and getting either comparison the wrong way
    // round moves every product sitting on a threshold.
    [InlineData(5.0, IndicatorLevel.Lower)]
    [InlineData(5.01, IndicatorLevel.Moderate)]
    [InlineData(22.5, IndicatorLevel.Moderate)]
    [InlineData(22.51, IndicatorLevel.Higher)]
    public void Sugar_lands_in_the_published_band(double sugars, IndicatorLevel expected)
    {
        var indicator = Single(Food(new NutritionValues { SugarsG = sugars }), "sugars_g");

        Assert.Equal(expected, indicator.Level);
    }

    [Fact]
    public void Parle_g_is_higher_in_sugar_than_the_published_threshold()
    {
        // A real product: 25.5 g sugar per 100 g, above the 22.5 g cut-off.
        var indicator = Single(Food(new NutritionValues { SugarsG = 25.5 }), "sugars_g");

        Assert.Equal(IndicatorLevel.Higher, indicator.Level);
        Assert.Contains("25.5 g per 100 g", indicator.Statement);
        Assert.Contains("above 22.5 g", indicator.Statement);
    }

    [Fact]
    public void Every_indicator_names_the_rule_and_version_that_produced_it()
    {
        // The definition of done for this feature: an indicator that cannot say where it came
        // from must not be displayable.
        var indicators = IndicatorEngine.Evaluate(
            Food(new NutritionValues { SugarsG = 10, FatG = 20, SaturatedFatG = 2, SodiumMg = 500 }),
            Rules);

        Assert.NotEmpty(indicators);

        foreach (var indicator in indicators)
        {
            Assert.False(string.IsNullOrWhiteSpace(indicator.RuleId));
            Assert.Matches(@"^\d+\.\d+\.\d+$", indicator.RuleVersion);
            Assert.False(string.IsNullOrWhiteSpace(indicator.RuleTitle));
            Assert.False(string.IsNullOrWhiteSpace(indicator.Source.Title));
            Assert.StartsWith("https://", indicator.Source.Url);
            Assert.False(string.IsNullOrWhiteSpace(indicator.Source.Locator));
        }
    }

    [Fact]
    public void Salt_is_derived_from_sodium_and_says_so()
    {
        // Labels declare sodium; the published thresholds are salt. 600 mg sodium is 1.5 g salt,
        // which is exactly the top of the moderate band.
        var indicator = Single(Food(new NutritionValues { SodiumMg = 600 }), "salt_g");

        Assert.Equal(IndicatorLevel.Moderate, indicator.Level);
        Assert.Equal(1.5, indicator.Value, 3);
        Assert.NotNull(indicator.DerivedNote);
        Assert.Contains("2.5", indicator.DerivedNote);
    }

    [Fact]
    public void A_nutrient_the_packet_does_not_declare_produces_no_indicator()
    {
        // Silence, not a zero. A missing value is not a low value, and the screen has to keep
        // those apart.
        var indicators = IndicatorEngine.Evaluate(Food(new NutritionValues { SugarsG = 10 }), Rules);

        Assert.DoesNotContain(indicators, i => i.Nutrient == "saturated_fat_g");
        Assert.DoesNotContain(indicators, i => i.Nutrient == "salt_g");
    }

    [Fact]
    public void Drink_thresholds_are_not_applied_to_food_or_the_reverse()
    {
        // 15 g sugar per 100: moderate for a biscuit, higher for a drink, because the drink
        // threshold is 11.25 against the food threshold of 22.5. Using one rule set for both
        // would call almost every soft drink moderate in sugar.
        var food = Single(Food(new NutritionValues { SugarsG = 15 }), "sugars_g");

        var drink = Single(
            new NutritionBlock { Basis = NutritionBasis.Per100ml, Values = new NutritionValues { SugarsG = 15 } },
            "sugars_g");

        Assert.Equal(IndicatorLevel.Moderate, food.Level);
        Assert.Equal(IndicatorLevel.Higher, drink.Level);
        Assert.Equal("fop-bands-food", food.RuleId);
        Assert.Equal("fop-bands-drink", drink.RuleId);
    }

    [Fact]
    public void A_per_serving_panel_is_scaled_and_labelled_as_calculated()
    {
        // 2,338 products in the catalogue declare only a per-serving panel with a serving mass.
        // Without this they would show a table and no indicators at all.
        var block = new NutritionBlock
        {
            Basis = NutritionBasis.PerServing,
            Serving = new ServingInfo
            {
                Description = "2 biscuits (25 g)",
                Quantity = new Quantity { Value = 25, Unit = Unit.Gram },
            },
            Values = new NutritionValues { SugarsG = 6.0 },
        };

        var indicator = Single(block, "sugars_g");

        // 6 g in 25 g is 24 g per 100 g, which is above the threshold even though the printed
        // number is below it.
        Assert.Equal(24.0, indicator.Value, 3);
        Assert.Equal(IndicatorLevel.Higher, indicator.Level);

        // ADR-0033: permitted at display time, but never presentable as a printed figure.
        Assert.True(indicator.IsCalculated);
        Assert.Contains("Calculated from the declared serving", indicator.Statement);
    }

    [Fact]
    public void A_serving_with_no_stated_mass_produces_no_indicators()
    {
        // "1 biscuit" is a description, not a mass. Assuming a weight for it is exactly the
        // invention the data rules forbid, so the honest result is no indicator.
        var block = new NutritionBlock
        {
            Basis = NutritionBasis.PerServing,
            Serving = new ServingInfo { Description = "1 biscuit" },
            Values = new NutritionValues { SugarsG = 6.0 },
        };

        Assert.Empty(IndicatorEngine.Evaluate(block, Rules));
    }

    [Fact]
    public void A_large_portion_can_be_higher_even_when_the_concentration_is_not()
    {
        // The source adds a per-portion test above 100 g. 20 g of sugar per 100 g is moderate,
        // but a 150 g portion carries 30 g, over the 27 g portion limit.
        var block = new NutritionBlock
        {
            Basis = NutritionBasis.Per100g,
            Serving = new ServingInfo
            {
                Description = "1 pack (150 g)",
                Quantity = new Quantity { Value = 150, Unit = Unit.Gram },
            },
            Values = new NutritionValues { SugarsG = 20 },
        };

        Assert.Equal(IndicatorLevel.Higher, Single(block, "sugars_g").Level);
    }

    [Fact]
    public void The_portion_test_does_not_apply_to_small_portions()
    {
        // The source restricts it to portions above 100 g, so a 30 g pack stays moderate.
        var block = new NutritionBlock
        {
            Basis = NutritionBasis.Per100g,
            Serving = new ServingInfo
            {
                Description = "1 pack (30 g)",
                Quantity = new Quantity { Value = 30, Unit = Unit.Gram },
            },
            Values = new NutritionValues { SugarsG = 20 },
        };

        Assert.Equal(IndicatorLevel.Moderate, Single(block, "sugars_g").Level);
    }

    [Fact]
    public void No_indicator_carries_a_health_verdict()
    {
        // DATA_POLICY.md section 7. Checked mechanically because it is the sort of thing that
        // creeps into copy one sentence at a time.
        string[] banned =
        [
            "healthy", "unhealthy", "good for", "bad for", "avoid", "safe", "unsafe",
            "harmful", "junk", "should not", "recommended",
        ];

        var indicators = IndicatorEngine.Evaluate(
            Food(new NutritionValues { SugarsG = 40, FatG = 30, SaturatedFatG = 15, SodiumMg = 900 }),
            Rules);

        Assert.NotEmpty(indicators);

        foreach (var indicator in indicators)
        {
            var text = (indicator.Statement + " " + indicator.RuleSummary + " " + indicator.RuleTitle)
                .ToLowerInvariant();

            foreach (var word in banned)
            {
                Assert.DoesNotContain(word, text);
            }
        }
    }
}
