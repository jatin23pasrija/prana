using Prana.Core.Model;

namespace Prana.Core.Rules;

/// <summary>
/// Reads a nutrient out of a panel by the key rule files use.
/// </summary>
/// <remarks>
/// Rule files name nutrients with the same keys the records use, so that a threshold is written
/// against the field it will be compared with and a typo cannot silently match nothing. The one
/// key that is not a stored field is <c>salt_g</c>, which rule files must reach through a
/// declared derivation; asking for it directly returns nothing rather than guessing.
/// </remarks>
public static class Nutrients
{
    public static double? Read(NutritionValues values, string nutrient) => nutrient switch
    {
        "energy_kcal" => values.EnergyKcal,
        "energy_kj" => values.EnergyKj,
        "protein_g" => values.ProteinG,
        "carbohydrate_g" => values.CarbohydrateG,
        "sugars_g" => values.SugarsG,
        "added_sugars_g" => values.AddedSugarsG,
        "fat_g" => values.FatG,
        "saturated_fat_g" => values.SaturatedFatG,
        "trans_fat_g" => values.TransFatG,
        "fibre_g" => values.FibreG,
        "sodium_mg" => values.SodiumMg,

        // Not stored on any record. A rule needing salt declares how to derive it from sodium,
        // which keeps the arithmetic in the cited rule file rather than hidden in here.
        _ => null,
    };

    /// <summary>Display name for a nutrient key, used where no rule supplies one.</summary>
    public static string DisplayName(string nutrient) => nutrient switch
    {
        "energy_kcal" => "Energy",
        "energy_kj" => "Energy",
        "protein_g" => "Protein",
        "carbohydrate_g" => "Carbohydrate",
        "sugars_g" => "Sugars",
        "added_sugars_g" => "Added sugars",
        "fat_g" => "Fat",
        "saturated_fat_g" => "Saturated fat",
        "trans_fat_g" => "Trans fat",
        "fibre_g" => "Fibre",
        "sodium_mg" => "Sodium",
        "salt_g" => "Salt",
        _ => nutrient,
    };

    /// <summary>The unit a stored nutrient is declared in.</summary>
    public static string UnitOf(string nutrient) => nutrient switch
    {
        "energy_kcal" => "kcal",
        "energy_kj" => "kJ",
        "sodium_mg" => "mg",
        _ => "g",
    };

    /// <summary>
    /// Every nutrient a panel may carry, in the order a nutrition panel prints them. Used to
    /// render the table, so that two products always list their nutrients the same way round.
    /// </summary>
    public static readonly IReadOnlyList<string> PanelOrder =
    [
        "energy_kcal",
        "energy_kj",
        "protein_g",
        "carbohydrate_g",
        "sugars_g",
        "added_sugars_g",
        "fat_g",
        "saturated_fat_g",
        "trans_fat_g",
        "fibre_g",
        "sodium_mg",
    ];
}
