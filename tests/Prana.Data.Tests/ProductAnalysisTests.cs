using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Core.Rules;
using Prana.Data;
using Xunit;

namespace Prana.Data.Tests;

/// <summary>
/// The product screen's behaviour, tested without a UI.
/// </summary>
/// <remarks>
/// These cover the definition of done for F10 directly. Three of them are rules that are easy to
/// break by accident and expensive when broken: an undeclared nutrient must render as unknown and
/// never as zero, a record holding only a name must still be marked incomplete so discovery stays
/// on offer, and no copy anywhere may carry a health verdict.
/// </remarks>
public sealed class ProductAnalysisTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Prana.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    /// <summary>Loads the rules that ship, so the test exercises the real thresholds.</summary>
    private sealed class ShippedRules : IRuleProvider
    {
        public Task<IReadOnlyList<RuleSet>> GetRulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RuleSet>>(
            [
                .. Directory
                    .EnumerateFiles(Path.Combine(RepositoryRoot(), "rules"), "*.json", SearchOption.AllDirectories)
                    .Select(f => PranaJson.Deserialize<RuleSet>(File.ReadAllText(f)))
            ]);
    }

    /// <summary>The real dictionary, with no peer statistics unless a test supplies them.</summary>
    private sealed class Dictionary(IReadOnlyList<PeerStat>? peers = null) : IAnalysisRepository
    {
        public Task<IReadOnlyList<IngredientRecord>> LoadDictionaryAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IngredientRecord>>(
            [
                .. Directory
                    .EnumerateFiles(Path.Combine(RepositoryRoot(), "data", "ingredients"), "*.json")
                    .Select(f => PranaJson.Deserialize<IngredientRecord>(File.ReadAllText(f)))
            ]);

        public Task<IReadOnlyList<PeerStat>> PeerStatsAsync(
            string categoryId, NutritionBasis basis, CancellationToken cancellationToken) =>
            Task.FromResult(peers ?? []);

        // No catalogue behind these tests, so slugs come back unresolved, which is the documented
        // fallback rather than a special case for testing.
        public Task<string?> DisplayNameAsync(
            string table, string id, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private static ProductAnalysis Analysis(IReadOnlyList<PeerStat>? peers = null) =>
        new(new ShippedRules(), new Dictionary(peers));

    private static ProductRecord Product(
        IReadOnlyList<NutritionBlock>? nutrition = null,
        string? ingredients = null,
        string lastVerified = "2026-09-01",
        string? category = null) => new()
        {
            SchemaVersion = 1,
            Gtin = "08901719134845",
            BarcodePrinted = "8901719134845",
            BarcodeFormat = BarcodeFormat.Ean13,
            Name = "Test Biscuit",
            Category = category,
            Countries = ["IN"],
            Nutrition = nutrition,
            IngredientsRaw = ingredients,
            Sources =
            [
                new Source { Id = "s1", Type = SourceType.OpenDatabase, RetrievedAt = "2026-09-01" },
            ],
            Provenance = new System.Collections.Generic.Dictionary<string, ProvenanceEntry>(StringComparer.Ordinal)
            {
                ["name"] = new() { Source = "s1", Confidence = Confidence.Medium },
            },
            Verification = new Verification
            {
                Status = VerificationStatus.Unverified,
                LastVerified = lastVerified,
            },
        };

    private static readonly DateOnly Today = new(2026, 9, 3);

    [Fact]
    public async Task An_undeclared_nutrient_is_unknown_and_never_zero()
    {
        // The single easiest thing to get wrong on this screen. Rendering a missing sugar figure
        // as 0 g would tell someone a sugary biscuit has no sugar.
        var product = Product([
            new NutritionBlock
            {
                Basis = NutritionBasis.Per100g,
                Values = new NutritionValues { EnergyKcal = 450, SugarsG = null },
                NotDeclared = ["sugars_g"],
            },
        ]);

        var result = await Analysis().AnalyseAsync(product, Today, TestContext.Current.CancellationToken);

        var sugar = Assert.Single(result.Panels[0].Rows, r => r.Nutrient == "sugars_g");

        Assert.Equal("Unknown", sugar.DisplayValue);
        Assert.False(sugar.IsKnown);
        Assert.DoesNotContain("0", sugar.DisplayValue);
    }

    [Fact]
    public async Task A_record_with_only_a_name_is_incomplete_and_still_offers_discovery()
    {
        // ADR-0026, and 58 per cent of the catalogue. A bare record makes the lookup succeed, so
        // without this the app would stop offering discovery for exactly the products that need
        // it most.
        var result = await Analysis().AnalyseAsync(Product(), Today, TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Panels);
        Assert.Equal(PalmState.NoIngredients, result.Palm.State);
    }

    [Fact]
    public async Task A_record_with_only_ingredients_counts_as_complete()
    {
        // Completeness is "we know something about what is in it", not "we have everything".
        var result = await Analysis().AnalyseAsync(
            Product(ingredients: "Wheat flour, sugar, palm oil"),
            Today,
            TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(PalmState.Present, result.Palm.State);
    }

    [Theory]
    [InlineData("2026-09-01", Freshness.Current)]
    [InlineData("2026-04-01", Freshness.Current)]
    [InlineData("2026-03-01", Freshness.ReviewRecommended)]
    [InlineData("2025-10-01", Freshness.ReviewRecommended)]
    [InlineData("2025-09-01", Freshness.PossiblyOutdated)]
    [InlineData("2019-01-01", Freshness.PossiblyOutdated)]
    public async Task The_freshness_thresholds_match_the_data_policy(string verified, Freshness expected)
    {
        // Six months and twelve months, from DATA_POLICY.md section 5. These only mean anything
        // because the importer stopped bumping the date on every run; see ADR-0032.
        var result = await Analysis().AnalyseAsync(
            Product(lastVerified: verified),
            Today,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Freshness);
    }

    [Fact]
    public async Task An_unparseable_verification_date_says_so_rather_than_claiming_freshness()
    {
        var result = await Analysis().AnalyseAsync(
            Product(lastVerified: "sometime"),
            Today,
            TestContext.Current.CancellationToken);

        Assert.Equal(Freshness.Unknown, result.Freshness);
        Assert.Contains("no verification date", result.FreshnessNote);
    }

    [Fact]
    public async Task A_peer_comparison_names_how_many_products_it_is_drawn_from()
    {
        // A comparison against 31 products and one against 3,100 look identical without this.
        var peers = new[]
        {
            new PeerStat("biscuits", "sugars_g", 276, 19.5, 32.9, "category-peers", "1.0.0"),
        };

        var product = Product(
            [new NutritionBlock { Basis = NutritionBasis.Per100g, Values = new NutritionValues { SugarsG = 25.5 } }],
            category: "biscuits");

        var result = await Analysis(peers).AnalyseAsync(product, Today, TestContext.Current.CancellationToken);

        var sentence = Assert.Single(result.PeerComparisons);

        Assert.Contains("276", sentence);

        // 25.5 sits between the 25th and 75th percentile for biscuits, so it is unremarkable
        // among biscuits, while the published threshold still calls it higher. Both statements
        // are true and the screen shows both.
        Assert.Contains("about the same as most", sentence);
        Assert.Equal(IndicatorLevel.Higher, Assert.Single(
            result.Panels[0].Indicators, i => i.Nutrient == "sugars_g").Level);
    }

    [Fact]
    public async Task A_product_with_no_category_gets_no_peer_comparison()
    {
        // 74 per cent of the catalogue. Silence is the correct output.
        var product = Product(
            [new NutritionBlock { Basis = NutritionBasis.Per100g, Values = new NutritionValues { SugarsG = 25.5 } }]);

        var result = await Analysis().AnalyseAsync(product, Today, TestContext.Current.CancellationToken);

        Assert.Empty(result.PeerComparisons);
    }

    [Fact]
    public async Task Nothing_the_screen_says_carries_a_health_verdict()
    {
        // DATA_POLICY.md section 7, checked over every sentence the analysis produces, because
        // this is the sort of thing that arrives one well-meaning word at a time.
        string[] banned =
        [
            "healthy", "unhealthy", "good for you", "bad for you", "you should", "avoid this",
            "harmful", "junk", "nutritious", "wholesome",
        ];

        var product = Product(
            [
                new NutritionBlock
                {
                    Basis = NutritionBasis.Per100g,
                    Values = new NutritionValues { SugarsG = 45, FatG = 30, SaturatedFatG = 18, SodiumMg = 1200 },
                },
            ],
            ingredients: "Refined wheat flour, sugar, hydrogenated palm oil, salt",
            lastVerified: "2019-01-01");

        var result = await Analysis().AnalyseAsync(product, Today, TestContext.Current.CancellationToken);

        var everything = string.Join(" ",
        [
            result.FreshnessNote,
            result.Palm.Statement,
            .. result.PeerComparisons,
            .. result.Panels.SelectMany(p => p.Indicators).Select(i => i.Statement),
            .. result.Panels.SelectMany(p => p.Indicators).Select(i => i.RuleSummary),
        ]).ToLowerInvariant();

        foreach (var word in banned)
        {
            Assert.DoesNotContain(word, everything);
        }
    }

    [Fact]
    public async Task Both_panels_of_a_two_panel_packet_are_kept_apart()
    {
        // A packet printing per 100 g and per serving produces two cards. Merging them would
        // require converting between bases, which ADR-0023 forbids.
        var product = Product(
        [
            new NutritionBlock
            {
                Basis = NutritionBasis.Per100g,
                Values = new NutritionValues { SugarsG = 25.5 },
            },
            new NutritionBlock
            {
                Basis = NutritionBasis.PerServing,
                Serving = new ServingInfo
                {
                    Description = "2 biscuits (25 g)",
                    Quantity = new Quantity { Value = 25, Unit = Unit.Gram },
                },
                Values = new NutritionValues { SugarsG = 6.4 },
            },
        ]);

        var result = await Analysis().AnalyseAsync(product, Today, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Panels.Count);
        Assert.Equal("Per 100 g", result.Panels[0].BasisLabel);
        Assert.Equal("Per serving", result.Panels[1].BasisLabel);
        Assert.Equal("2 biscuits (25 g)", result.Panels[1].ServingLabel);

        // The per-serving panel's indicators are calculated; the per-100 g ones are not.
        Assert.All(result.Panels[0].Indicators, i => Assert.False(i.IsCalculated));
        Assert.All(result.Panels[1].Indicators, i => Assert.True(i.IsCalculated));
    }
}
