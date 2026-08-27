using Prana.Core.Model;
using Xunit;

namespace Prana.Tools.Validator.Tests;

/// <summary>
/// One passing case and one failing case for every rule, exercised through the whole tool.
/// </summary>
public sealed class RuleTests
{
    private static void AssertReports(IReadOnlyList<Diagnostic> diagnostics, string code, Severity severity)
    {
        var match = diagnostics.FirstOrDefault(d => d.Code == code);

        Assert.True(
            match is not null,
            $"Expected {code}. Got: {(diagnostics.Count == 0 ? "nothing" : string.Join("; ", diagnostics.Select(d => $"{d.Code} {d.Message}")))}");

        Assert.Equal(severity, match!.Severity);
        Assert.True(match.Line > 0, $"{code} was reported without a line number, so it cannot be annotated.");
    }

    private static void AssertSilent(IReadOnlyList<Diagnostic> diagnostics, string code) =>
        Assert.DoesNotContain(diagnostics, d => d.Code == code);

    // ---------------------------------------------------------------- baseline

    [Fact]
    public void A_good_record_produces_no_errors_or_warnings()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel()],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        var noisy = diagnostics.Where(d => d.Severity >= Severity.Warning).ToList();

        Assert.True(
            noisy.Count == 0,
            "A known-good record should be silent. Got: "
                + string.Join("; ", noisy.Select(d => $"{d.Code} at {d.Pointer}: {d.Message}")));
    }

    // ---------------------------------------------------------------- identity

    [Fact]
    public void Bad_check_digit_is_an_error()
    {
        using var harness = new ValidatorHarness();

        // 8901234567891 is the valid barcode with its last digit changed, the classic typo.
        harness.Write("data/products/890/08901234567891.json", ReadTemplate(
            gtin: "08901234567891", printed: "8901234567891"));

        AssertReports(harness.Run(), Rules.BadCheckDigit, Severity.Error);
    }

    [Fact]
    public void A_gtin_that_is_not_the_padded_barcode_is_an_error()
    {
        using var harness = new ValidatorHarness();

        // A real barcode, but padded to the wrong key. This is what silently creates duplicates.
        harness.Write("data/products/890/08901111111116.json", ReadTemplate(
            gtin: "08901111111116", printed: "8901234567890"));

        AssertReports(harness.Run(), Rules.GtinNotCanonical, Severity.Error);
    }

    [Fact]
    public void A_format_that_does_not_match_the_barcode_length_is_a_warning()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(format: BarcodeFormat.Ean8));

        AssertReports(diagnostics, Rules.BarcodeFormatMismatch, Severity.Warning);
    }

    [Fact]
    public void A_record_filed_in_the_wrong_place_is_an_error()
    {
        using var harness = new ValidatorHarness();

        harness.Write("data/products/111/08901234567890.json", ReadTemplate());

        AssertReports(harness.Run(), Rules.WrongFileLocation, Severity.Error);
    }

    // ---------------------------------------------------------------- nutrition

    [Fact]
    public void Saturated_fat_above_total_fat_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(fat: 10, saturatedFat: 12, energyKcal: 380)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.SaturatedFatExceedsFat, Severity.Error);
    }

    [Fact]
    public void Label_rounding_is_tolerated()
    {
        using var harness = new ValidatorHarness();

        // Both values rounded to one decimal place. This is a real packet, not a contradiction,
        // and rejecting it would teach contributors that CI is wrong more often than they are.
        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(fat: 10.4, saturatedFat: 10.5, energyKcal: 390)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertSilent(diagnostics, Rules.SaturatedFatExceedsFat);
    }

    [Fact]
    public void Sugars_above_carbohydrate_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(carbohydrate: 20, sugars: 30, energyKcal: 320)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.SugarsExceedCarbohydrate, Severity.Error);
    }

    [Fact]
    public void Added_sugars_above_total_sugars_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(sugars: 18, addedSugars: 25)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.AddedSugarsExceedSugars, Severity.Error);
    }

    [Fact]
    public void Trans_fat_above_total_fat_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(fat: 20, transFat: 25)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.TransFatExceedsFat, Severity.Error);
    }

    [Fact]
    public void Macros_weighing_more_than_the_basis_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition:
            [
                ValidProduct.Panel(protein: 40, carbohydrate: 40, sugars: 10, fat: 40, saturatedFat: 10, energyKcal: 680)
            ],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.MacrosExceedBasis, Severity.Error);
    }

    [Fact]
    public void Energy_that_disagrees_with_the_macros_is_only_a_warning()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(energyKcal: 100)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        // A warning, never an error: Indian labels differ on whether fibre counts inside
        // carbohydrate, so this is a prompt to look rather than proof of a mistake.
        AssertReports(diagnostics, Rules.EnergyDisagreesWithMacros, Severity.Warning);
    }

    [Fact]
    public void Energy_within_tolerance_is_not_reported()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(energyKcal: 476)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertSilent(diagnostics, Rules.EnergyDisagreesWithMacros);
    }

    [Fact]
    public void Kcal_and_kilojoules_that_do_not_convert_is_a_warning()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(energyKcal: 476, energyKj: 800)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.EnergyUnitsDisagree, Severity.Warning);
    }

    [Fact]
    public void Two_panels_for_the_same_basis_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(), ValidProduct.Panel()],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.DuplicateBasis, Severity.Error);
    }

    [Fact]
    public void A_serving_with_no_mass_is_a_warning()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition:
            [
                ValidProduct.Panel(
                    basis: NutritionBasis.PerServing,
                    serving: new ServingInfo { Description = "2 biscuits" },
                    fat: 5, saturatedFat: 2.5, carbohydrate: 17, sugars: 4.5, protein: 1.5,
                    fibre: 0.8, energyKcal: 119)
            ],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.ServingWithoutMass, Severity.Warning);
    }

    [Fact]
    public void A_nutrient_both_declared_and_marked_absent_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(notDeclared: ["sugars_g"])],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.NotDeclaredButPresent, Severity.Error);
    }

    [Fact]
    public void Sodium_no_food_could_contain_is_a_warning()
    {
        using var harness = new ValidatorHarness();

        // 40,000 mg per 100 g is about 100 g of salt in 100 g of food. A cumin powder in the
        // first real import declared exactly this, and it passed only because it sat on the
        // schema ceiling.
        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(sodiumMg: 40000)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertReports(diagnostics, Rules.SodiumImplausible, Severity.Warning);
    }

    [Fact]
    public void A_genuinely_salty_product_is_not_flagged()
    {
        using var harness = new ValidatorHarness();

        // Namkeen at 1,600 mg per 100 g is high and entirely real. The rule exists to catch a
        // misplaced decimal point, not to comment on salty food.
        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel(sodiumMg: 1600)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertSilent(diagnostics, Rules.SodiumImplausible);
    }

    [Fact]
    public void A_bag_of_salt_is_not_flagged_for_containing_salt()
    {
        using var harness = new ValidatorHarness();

        // Table salt really is about 39 g of sodium per 100 g. Warning about it would be noise,
        // and noise is how people learn to ignore warnings that matter.
        var diagnostics = harness.Validate(ValidProduct.Build(
            category: "salt",
            nutrition: [ValidProduct.Panel(sodiumMg: 38800)],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertSilent(diagnostics, Rules.SodiumImplausible);
    }

    // ---------------------------------------------------------------- provenance

    [Fact]
    public void A_value_with_no_covering_source_is_an_error()
    {
        using var harness = new ValidatorHarness();

        // Nutrition is declared, but the provenance map only covers the name.
        var diagnostics = harness.Validate(ValidProduct.Build(nutrition: [ValidProduct.Panel()]));

        AssertReports(diagnostics, Rules.UncoveredValue, Severity.Error);
    }

    [Fact]
    public void One_entry_covers_everything_beneath_it()
    {
        using var harness = new ValidatorHarness();

        // The whole point of ADR-0018: a single "nutrition" entry backs every value in every
        // panel, because the whole panel really did come from one photograph.
        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel()],
            provenance: ValidProduct.ProvenanceWithNutrition()));

        AssertSilent(diagnostics, Rules.UncoveredValue);
    }

    [Fact]
    public void A_more_specific_path_also_counts_as_coverage()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel()],
            provenance: new Dictionary<string, ProvenanceEntry>
            {
                ["name"] = new() { Source = "s1", Confidence = Confidence.High },
                ["nutrition[0]"] = new() { Source = "s1", Confidence = Confidence.High },
            }));

        AssertSilent(diagnostics, Rules.UncoveredValue);
    }

    [Fact]
    public void Provenance_pointing_at_an_undeclared_source_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            provenance: new Dictionary<string, ProvenanceEntry>
            {
                ["name"] = new() { Source = "s9", Confidence = Confidence.High },
            }));

        AssertReports(diagnostics, Rules.UnknownSourceReference, Severity.Error);
    }

    [Fact]
    public void Provenance_for_a_field_that_no_longer_exists_is_a_warning()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            provenance: new Dictionary<string, ProvenanceEntry>
            {
                ["name"] = new() { Source = "s1", Confidence = Confidence.High },
                ["ingredients_raw"] = new() { Source = "s1", Confidence = Confidence.High },
            }));

        AssertReports(diagnostics, Rules.StaleProvenancePath, Severity.Warning);
    }

    [Fact]
    public void A_source_nothing_references_is_only_a_note()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            sources:
            [
                ValidProduct.OneSource,
                new Source { Id = "s2", Type = SourceType.Retailer, RetrievedAt = "2026-08-02" },
            ]));

        AssertReports(diagnostics, Rules.UnusedSource, Severity.Info);
    }

    [Fact]
    public void A_verified_record_cannot_rest_on_low_confidence_evidence()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel()],
            provenance: ValidProduct.ProvenanceWithNutrition(Confidence.Low),
            verification: new Verification
            {
                Status = VerificationStatus.Verified,
                LastVerified = "2026-08-01",
            }));

        AssertReports(diagnostics, Rules.VerifiedWithLowConfidence, Severity.Error);
    }

    [Fact]
    public void A_verified_record_cannot_have_an_unresolved_conflict()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            nutrition: [ValidProduct.Panel()],
            provenance: ValidProduct.ProvenanceWithNutrition(),
            conflicts:
            [
                new Conflict
                {
                    Path = "nutrition[0].values.sugars_g",
                    Values =
                    [
                        new ConflictValue { Source = "s1", Value = Number(18) },
                        new ConflictValue { Source = "s1", Value = Number(21) },
                    ],
                    Resolution = ConflictResolution.Unresolved,
                }
            ],
            verification: new Verification
            {
                Status = VerificationStatus.Verified,
                LastVerified = "2026-08-01",
            }));

        AssertReports(diagnostics, Rules.VerifiedWithUnresolvedConflict, Severity.Error);
    }

    // ---------------------------------------------------------------- ingredients

    [Fact]
    public void A_parsed_ingredient_list_without_its_raw_text_is_an_error()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            ingredients: [new Ingredient { Raw = "Sugar", Canonical = "sugar" }],
            provenance: new Dictionary<string, ProvenanceEntry>
            {
                ["name"] = new() { Source = "s1", Confidence = Confidence.High },
                ["ingredients"] = new() { Source = "s1", Confidence = Confidence.High },
            }));

        AssertReports(diagnostics, Rules.ParsedWithoutRaw, Severity.Error);
    }

    [Fact]
    public void An_unmatched_canonical_ingredient_is_only_a_note()
    {
        using var harness = new ValidatorHarness();

        var diagnostics = harness.Validate(ValidProduct.Build(
            ingredientsRaw: "Sugar",
            ingredients: [new Ingredient { Raw = "Sugar", Canonical = "sugar" }],
            provenance: new Dictionary<string, ProvenanceEntry>
            {
                ["name"] = new() { Source = "s1", Confidence = Confidence.High },
                ["ingredients_raw"] = new() { Source = "s1", Confidence = Confidence.High },
                ["ingredients"] = new() { Source = "s1", Confidence = Confidence.High },
            }));

        AssertReports(diagnostics, Rules.UnmatchedIngredient, Severity.Info);
    }

    // ---------------------------------------------------------------- cross record

    [Fact]
    public void The_same_barcode_in_two_files_is_an_error()
    {
        using var harness = new ValidatorHarness();

        harness.Write("data/products/890/08901234567890.json", ReadTemplate());
        harness.Write("data/products/890/copy.json", ReadTemplate());

        AssertReports(harness.Run(), Rules.DuplicateGtin, Severity.Error);
    }

    [Fact]
    public void A_country_with_no_record_is_a_warning()
    {
        using var harness = new ValidatorHarness();

        var product = ValidProduct.Build();
        harness.Write(
            $"data/products/890/{product.Gtin}.json",
            Core.Json.PranaJson.Serialize(ValidProduct.Build()).Replace("\"IN\"", "\"FR\""));

        AssertReports(harness.Run(), Rules.UnknownCountry, Severity.Warning);
    }

    [Fact]
    public void A_file_that_is_not_json_is_an_error()
    {
        using var harness = new ValidatorHarness();

        harness.Write("data/products/890/08901234567890.json", "{ this is not json");

        AssertReports(harness.Run(), Rules.InvalidJson, Severity.Error);
    }

    [Fact]
    public void A_badly_formatted_file_is_a_warning_that_names_the_fix()
    {
        using var harness = new ValidatorHarness();

        harness.Write("data/products/890/08901234567890.json", CompactTemplate());

        var diagnostics = harness.Run();

        AssertReports(diagnostics, Rules.NotCanonicalFormat, Severity.Warning);
        Assert.Contains("format", diagnostics.First(d => d.Code == Rules.NotCanonicalFormat).Message);
    }

    // ---------------------------------------------------------------- helpers

    private static System.Text.Json.JsonElement Number(double value) =>
        System.Text.Json.JsonDocument.Parse(value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .RootElement.Clone();

    private static string ReadTemplate(string gtin = ValidProduct.Gtin, string printed = ValidProduct.Printed) =>
        Core.Json.PranaJson.Serialize(ValidProduct.Build(gtin: gtin, printed: printed));

    private static string CompactTemplate() =>
        System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonDocument.Parse(ReadTemplate()).RootElement);
}
