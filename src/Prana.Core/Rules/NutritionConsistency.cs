using Prana.Core.Model;

namespace Prana.Core.Rules;

/// <summary>What kind of inconsistency was found in a declared nutrition panel.</summary>
public enum ConsistencyCode
{
    /// <summary>Saturated fat is part of total fat, so it cannot exceed it.</summary>
    SaturatedFatExceedsFat,

    /// <summary>Sugars are part of total carbohydrate, so they cannot exceed it.</summary>
    SugarsExceedCarbohydrate,

    /// <summary>Added sugars are part of total sugars, so they cannot exceed them.</summary>
    AddedSugarsExceedSugars,

    /// <summary>Trans fat is part of total fat, so it cannot exceed it.</summary>
    TransFatExceedsFat,

    /// <summary>The macronutrients add up to more mass than the basis they are declared against.</summary>
    MacrosExceedBasis,

    /// <summary>Declared energy does not match what the macronutrients imply.</summary>
    EnergyDisagreesWithMacros,

    /// <summary>The kcal and kJ figures do not convert into each other.</summary>
    EnergyUnitsDisagree,

    /// <summary>A nutrient is both declared and listed as not declared.</summary>
    NotDeclaredButPresent,

    /// <summary>More sodium than the food could physically contain.</summary>
    SodiumImplausible,
}

/// <summary>One inconsistency, with enough detail to explain it to a person.</summary>
/// <param name="Code">Which rule was broken.</param>
/// <param name="Path">JSON Pointer into the record, for annotating the exact line.</param>
/// <param name="Message">Plain English, safe to show to a user of the app.</param>
/// <param name="Declared">The value the packet states, where the rule concerns one.</param>
/// <param name="Expected">What the other declared values imply, where the rule computes one.</param>
public sealed record ConsistencyFinding(
    ConsistencyCode Code,
    string Path,
    string Message,
    double? Declared = null,
    double? Expected = null);

/// <summary>
/// Arithmetic checks on a declared nutrition panel.
/// </summary>
/// <remarks>
/// This lives in Prana.Core rather than in the validator because the app shows these findings
/// too. A product whose declared calories do not match its macronutrients is worth telling the
/// user about, and the app must reach exactly the same conclusion CI did.
///
/// Nothing here is stored in the record. Findings are recomputed from the declared values every
/// time, so they can never become stale, and so a correction to a nutrition value immediately
/// changes what the app says without a rebuild.
///
/// These rules check a panel against itself. They cannot tell whether the packet is right, only
/// whether it is internally consistent.
/// </remarks>
public static class NutritionConsistency
{
    /// <summary>
    /// Slack allowed on any mass comparison, in grams. Labels round to one decimal place, so a
    /// packet can legitimately print saturated fat as 10.5 g against total fat of 10.4 g. Any
    /// breach larger than this is a real contradiction rather than rounding.
    /// </summary>
    public const double MassToleranceG = 0.1;

    /// <summary>
    /// Slack allowed when comparing declared energy against the energy the macronutrients
    /// imply. Wide on purpose: Indian labels differ in whether fibre is counted inside
    /// carbohydrate, and sugar alcohols and organic acids are not declared at all. This finding
    /// is a prompt to look, never a statement that the packet is wrong.
    /// </summary>
    public const double EnergyTolerance = 0.20;

    /// <summary>Slack allowed when converting between kcal and kJ.</summary>
    public const double EnergyUnitTolerance = 0.05;

    /// <summary>
    /// Sodium above this, per 100 g or 100 ml, is worth questioning.
    /// </summary>
    /// <remarks>
    /// Table salt is roughly 39 g of sodium per 100 g, so nothing can exceed that and only salt
    /// itself comes close. 10 g is about 25 g of salt in every 100 g of food, which real products
    /// essentially never reach outside salt, bouillon and some spice blends.
    ///
    /// The threshold is deliberately far above anything a normal food reaches, because this
    /// exists to catch a decimal point in the wrong place rather than to comment on salty food.
    /// A cumin powder in the first import declared 40,000 mg, which is more salt than cumin.
    /// </remarks>
    public const double ImplausibleSodiumMgPer100 = 10_000;

    /// <summary>Kilojoules in one kilocalorie.</summary>
    public const double KilojoulesPerKilocalorie = 4.184;

    // Atwater factors. Fibre is counted at 2 kcal/g, which is the figure Indian and EU
    // labelling both use for the fermentable fraction.
    private const double KcalPerGramProtein = 4;
    private const double KcalPerGramCarbohydrate = 4;
    private const double KcalPerGramFat = 9;
    private const double KcalPerGramFibre = 2;

    /// <summary>Checks every block in a product and returns everything that does not add up.</summary>
    public static IReadOnlyList<ConsistencyFinding> Check(ProductRecord product)
    {
        ArgumentNullException.ThrowIfNull(product);

        if (product.Nutrition is null)
        {
            return [];
        }

        var findings = new List<ConsistencyFinding>();

        for (var i = 0; i < product.Nutrition.Count; i++)
        {
            findings.AddRange(Check(product.Nutrition[i], i));
        }

        return findings;
    }

    /// <summary>Checks one panel. <paramref name="index"/> is only used to build the path.</summary>
    public static IReadOnlyList<ConsistencyFinding> Check(NutritionBlock block, int index = 0)
    {
        ArgumentNullException.ThrowIfNull(block);

        var findings = new List<ConsistencyFinding>();
        var values = block.Values;
        var at = $"/nutrition/{index}";

        CheckPart(findings, values.SaturatedFatG, values.FatG, $"{at}/values/saturated_fat_g",
            ConsistencyCode.SaturatedFatExceedsFat,
            "Saturated fat is higher than total fat, and saturated fat is part of total fat.");

        CheckPart(findings, values.TransFatG, values.FatG, $"{at}/values/trans_fat_g",
            ConsistencyCode.TransFatExceedsFat,
            "Trans fat is higher than total fat, and trans fat is part of total fat.");

        CheckPart(findings, values.SugarsG, values.CarbohydrateG, $"{at}/values/sugars_g",
            ConsistencyCode.SugarsExceedCarbohydrate,
            "Sugars are higher than total carbohydrate, and sugars are part of total carbohydrate.");

        CheckPart(findings, values.AddedSugarsG, values.SugarsG, $"{at}/values/added_sugars_g",
            ConsistencyCode.AddedSugarsExceedSugars,
            "Added sugars are higher than total sugars, and added sugars are part of total sugars.");

        CheckMassBudget(findings, block, at);
        CheckEnergyAgainstMacros(findings, block, at);
        CheckEnergyUnits(findings, values, at);
        CheckSodium(findings, block, at);
        CheckNotDeclared(findings, block, at);

        return findings;
    }

    private static void CheckPart(
        List<ConsistencyFinding> findings,
        double? part,
        double? whole,
        string path,
        ConsistencyCode code,
        string message)
    {
        if (part is null || whole is null || part <= whole + MassToleranceG)
        {
            return;
        }

        findings.Add(new ConsistencyFinding(code, path, message, part, whole));
    }

    /// <summary>
    /// Protein, carbohydrate and fat cannot weigh more than the basis they are declared against.
    /// Only applies to a mass or volume basis, because a per-serving panel is measured against a
    /// serving that may be any size.
    /// </summary>
    private static void CheckMassBudget(List<ConsistencyFinding> findings, NutritionBlock block, string at)
    {
        if (block.Basis is not (NutritionBasis.Per100g or NutritionBasis.Per100ml))
        {
            return;
        }

        var values = block.Values;
        var total = (values.ProteinG ?? 0) + (values.CarbohydrateG ?? 0) + (values.FatG ?? 0);

        // Fibre is often declared separately from carbohydrate on Indian labels, so it is only
        // counted when carbohydrate is absent. Counting it twice would produce false alarms.
        if (values.CarbohydrateG is null)
        {
            total += values.FibreG ?? 0;
        }

        // A whole gram of slack, because three values each rounded to one decimal place can
        // legitimately drift further than a single comparison can.
        if (total <= 100 + 1.0)
        {
            return;
        }

        findings.Add(new ConsistencyFinding(
            ConsistencyCode.MacrosExceedBasis,
            $"{at}/values",
            $"Protein, carbohydrate and fat add up to {total:0.#} g, which is more than the "
                + $"{(block.Basis == NutritionBasis.Per100g ? "100 g" : "100 ml")} they are declared against.",
            total,
            100));
    }

    private static void CheckEnergyAgainstMacros(List<ConsistencyFinding> findings, NutritionBlock block, string at)
    {
        var values = block.Values;

        if (values.EnergyKcal is not { } declared || declared <= 0)
        {
            return;
        }

        // Without any macronutrient there is nothing to compare against, and a panel declaring
        // only energy is common enough that flagging it would be noise.
        if (values.ProteinG is null && values.CarbohydrateG is null && values.FatG is null)
        {
            return;
        }

        var calculated =
            ((values.ProteinG ?? 0) * KcalPerGramProtein)
            + ((values.CarbohydrateG ?? 0) * KcalPerGramCarbohydrate)
            + ((values.FatG ?? 0) * KcalPerGramFat)
            + (values.CarbohydrateG is null ? (values.FibreG ?? 0) * KcalPerGramFibre : 0);

        if (calculated <= 0)
        {
            return;
        }

        var drift = Math.Abs(declared - calculated) / calculated;

        if (drift <= EnergyTolerance)
        {
            return;
        }

        findings.Add(new ConsistencyFinding(
            ConsistencyCode.EnergyDisagreesWithMacros,
            $"{at}/values/energy_kcal",
            $"The packet declares {declared:0.#} kcal, but the protein, carbohydrate and fat it "
                + $"lists work out to about {calculated:0.#} kcal, a difference of {drift * 100:0}%.",
            declared,
            calculated));
    }

    private static void CheckEnergyUnits(List<ConsistencyFinding> findings, NutritionValues values, string at)
    {
        if (values.EnergyKcal is not { } kcal || values.EnergyKj is not { } kj || kcal <= 0)
        {
            return;
        }

        var expected = kcal * KilojoulesPerKilocalorie;
        var drift = Math.Abs(kj - expected) / expected;

        if (drift <= EnergyUnitTolerance)
        {
            return;
        }

        findings.Add(new ConsistencyFinding(
            ConsistencyCode.EnergyUnitsDisagree,
            $"{at}/values/energy_kj",
            $"{kcal:0.#} kcal is about {expected:0} kJ, but the record says {kj:0.#} kJ.",
            kj,
            expected));
    }

    /// <summary>
    /// Catches a sodium figure larger than the food could physically contain.
    /// </summary>
    /// <remarks>
    /// Only applied to a mass or volume basis. A per-serving panel is measured against a serving
    /// of unknown size, so a large absolute figure there says nothing.
    /// </remarks>
    private static void CheckSodium(List<ConsistencyFinding> findings, NutritionBlock block, string at)
    {
        if (block.Basis is not (NutritionBasis.Per100g or NutritionBasis.Per100ml))
        {
            return;
        }

        if (block.Values.SodiumMg is not { } sodium || sodium <= ImplausibleSodiumMgPer100)
        {
            return;
        }

        var asSalt = sodium * 2.54 / 1000;

        findings.Add(new ConsistencyFinding(
            ConsistencyCode.SodiumImplausible,
            $"{at}/values/sodium_mg",
            $"{sodium:0} mg of sodium per 100 is about {asSalt:0.#} g of salt in every 100, which no "
                + "food except salt itself contains. This is usually a decimal point in the wrong place.",
            sodium,
            ImplausibleSodiumMgPer100));
    }

    /// <summary>
    /// A nutrient cannot be both declared and confirmed absent. This usually means someone
    /// filled in a value later and forgot to remove the earlier note.
    /// </summary>
    private static void CheckNotDeclared(List<ConsistencyFinding> findings, NutritionBlock block, string at)
    {
        if (block.NotDeclared is not { Count: > 0 })
        {
            return;
        }

        var declared = DeclaredFieldNames(block.Values);

        for (var i = 0; i < block.NotDeclared.Count; i++)
        {
            var field = block.NotDeclared[i];

            if (!declared.Contains(field))
            {
                continue;
            }

            findings.Add(new ConsistencyFinding(
                ConsistencyCode.NotDeclaredButPresent,
                $"{at}/not_declared/{i}",
                $"{field} is listed as not declared on the packet, but the record also gives a value for it."));
        }
    }

    /// <summary>The wire names of every nutrient that carries a value in this panel.</summary>
    public static IReadOnlySet<string> DeclaredFieldNames(NutritionValues values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var names = new HashSet<string>(StringComparer.Ordinal);

        Add(names, "energy_kcal", values.EnergyKcal);
        Add(names, "energy_kj", values.EnergyKj);
        Add(names, "protein_g", values.ProteinG);
        Add(names, "carbohydrate_g", values.CarbohydrateG);
        Add(names, "sugars_g", values.SugarsG);
        Add(names, "added_sugars_g", values.AddedSugarsG);
        Add(names, "fat_g", values.FatG);
        Add(names, "saturated_fat_g", values.SaturatedFatG);
        Add(names, "trans_fat_g", values.TransFatG);
        Add(names, "fibre_g", values.FibreG);
        Add(names, "sodium_mg", values.SodiumMg);

        return names;

        static void Add(HashSet<string> target, string name, double? value)
        {
            if (value is not null)
            {
                target.Add(name);
            }
        }
    }
}
