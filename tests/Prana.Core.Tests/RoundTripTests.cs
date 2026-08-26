using Prana.Core.Json;
using Prana.Core.Model;

using Xunit;

namespace Prana.Core.Tests;

/// <summary>
/// Records live in Git and are edited by people. Any tool that rewrites a record must produce
/// exactly the bytes a contributor would have written, or every automated touch creates a
/// noisy diff and review becomes impossible. These tests hold the model and the JSON contract
/// to that standard against the real example files.
/// </summary>
public sealed class RoundTripTests
{
    public static TheoryData<string> ValidExamples()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(RepositoryPaths.ValidExamples, "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidExamples))]
    public void Record_survives_a_round_trip_byte_for_byte(string fileName)
    {
        var path = Path.Combine(RepositoryPaths.ValidExamples, fileName);

        // Git normalises line endings on checkout, so compare against LF regardless of what
        // is on disk on this machine.
        var original = File.ReadAllText(path).Replace("\r\n", "\n");

        var rewritten = RepositoryPaths.SchemaNameFor(path) switch
        {
            "product" => PranaJson.Serialize(PranaJson.Deserialize<ProductRecord>(original)),
            "ingredient" => PranaJson.Serialize(PranaJson.Deserialize<IngredientRecord>(original)),
            "brand" => PranaJson.Serialize(PranaJson.Deserialize<BrandRecord>(original)),
            "category" => PranaJson.Serialize(PranaJson.Deserialize<CategoryRecord>(original)),
            "country" => PranaJson.Serialize(PranaJson.Deserialize<CountryRecord>(original)),
            "alternative" => PranaJson.Serialize(PranaJson.Deserialize<AlternativeRecord>(original)),
            var other => throw new InvalidOperationException($"No model is mapped to {other}."),
        };

        Assert.Equal(original, rewritten);
    }

    [Fact]
    public void Unknown_nutrients_are_absent_rather_than_null()
    {
        var record = new NutritionBlock
        {
            Basis = NutritionBasis.Per100g,
            Values = new NutritionValues { SugarsG = 18 },
        };

        var json = PranaJson.Serialize(record);

        Assert.Contains("\"sugars_g\": 18", json);

        // Writing null would make "the packet does not declare it" and "nobody has researched
        // it" indistinguishable, which the whole unknown model depends on keeping apart.
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("fibre_g", json);
    }

    [Fact]
    public void Not_declared_is_what_separates_checked_from_unresearched()
    {
        var checkedAndAbsent = new NutritionBlock
        {
            Basis = NutritionBasis.Per100g,
            Values = new NutritionValues { SugarsG = 18 },
            NotDeclared = ["fibre_g"],
        };

        var neverLookedAt = new NutritionBlock
        {
            Basis = NutritionBasis.Per100g,
            Values = new NutritionValues { SugarsG = 18 },
        };

        Assert.NotEqual(PranaJson.Serialize(checkedAndAbsent), PranaJson.Serialize(neverLookedAt));
        Assert.Contains("not_declared", PranaJson.Serialize(checkedAndAbsent));
        Assert.DoesNotContain("not_declared", PranaJson.Serialize(neverLookedAt));
    }

    [Fact]
    public void A_field_the_schema_does_not_know_about_is_an_error_not_a_shrug()
    {
        const string json = """
            {
              "basis": "per_100g",
              "values": { "sugars_g": 18 },
              "vitamin_c_mg": 12
            }
            """;

        Assert.ThrowsAny<System.Text.Json.JsonException>(
            () => PranaJson.Deserialize<NutritionBlock>(json));
    }

    [Fact]
    public void Enum_values_use_the_wire_names_from_the_schema()
    {
        var record = new NutritionBlock
        {
            Basis = NutritionBasis.Per100ml,
            Values = new NutritionValues { SugarsG = 5 },
        };

        Assert.Contains("\"per_100ml\"", PranaJson.Serialize(record));
    }
}
