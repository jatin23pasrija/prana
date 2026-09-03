using Prana.Core.Model;

namespace Prana.Core.Rules;

/// <summary>
/// A panel expressed per 100 g or per 100 ml, together with how it got there.
/// </summary>
/// <param name="Basis">Which of the two it is.</param>
/// <param name="Form">Food or drink, decided by the unit the packet used.</param>
/// <param name="Factor">What each stored value is multiplied by to reach the basis.</param>
/// <param name="Calculated">
/// True when the panel was declared per serving and scaled here. Every figure derived from it
/// must be labelled, because it is arithmetic of ours and not a number printed on the packet.
/// </param>
/// <param name="ServingDescription">The serving that was scaled from, for the label.</param>
/// <param name="ServingAmount">The serving size in the basis unit, used for portion tests.</param>
public sealed record NormalisedPanel(
    RuleBasis Basis,
    ProductForm Form,
    double Factor,
    bool Calculated,
    string? ServingDescription,
    double? ServingAmount)
{
    /// <summary>The value of one nutrient on the normalised basis, or null if not declared.</summary>
    public double? Read(NutritionValues values, string nutrient)
    {
        var raw = Nutrients.Read(values, nutrient);
        return raw is null ? null : raw.Value * Factor;
    }
}

/// <summary>
/// Works out whether a panel can be compared with a per-100 threshold, and how.
/// </summary>
/// <remarks>
/// ADR-0023 forbids the importer converting between nutrition bases, and that has not changed:
/// nothing computed here is ever written to a record. ADR-0033 permits the conversion at display
/// time, under two conditions this class exists to guarantee. The serving mass must be declared
/// on the packet, so the arithmetic uses the manufacturer's own number rather than an assumed
/// serving. And the result must be marked as calculated, so it is never mistaken for a figure
/// that was printed.
///
/// The prize is real: 2,338 products declare a per-serving panel with a serving mass, and
/// without this they would show a nutrition table and no indicators at all.
///
/// Form is decided by the unit rather than the category, because 74 per cent of the catalogue
/// has no category, and a packet that states its serving in millilitres has told us it is a
/// drink whether or not anyone has categorised it.
/// </remarks>
public static class PanelBasis
{
    /// <summary>
    /// A serving has to be a real measured amount to scale from. "1 biscuit" is a description,
    /// not a mass, and inventing a mass for it is exactly what the data rules forbid.
    /// </summary>
    private static double? InBaseUnit(Quantity quantity) => quantity.Unit switch
    {
        Unit.Gram or Unit.Millilitre => quantity.Value,
        Unit.Kilogram or Unit.Litre => quantity.Value * 1000,
        _ => null,
    };

    private static ProductForm FormOf(Unit unit) =>
        unit is Unit.Millilitre or Unit.Litre ? ProductForm.Drink : ProductForm.Food;

    /// <summary>
    /// Returns how to read this panel on a per-100 basis, or null when it cannot be compared
    /// with anything. Returning null is a real answer: it is what happens to a per-serving panel
    /// whose packet never says how big a serving is.
    /// </summary>
    public static NormalisedPanel? Normalise(NutritionBlock block)
    {
        switch (block.Basis)
        {
            case NutritionBasis.Per100g:
                return new NormalisedPanel(
                    RuleBasis.Per100g, ProductForm.Food, 1.0, false, null,
                    ServingAmount: ServingIn(block, Unit.Gram));

            case NutritionBasis.Per100ml:
                return new NormalisedPanel(
                    RuleBasis.Per100ml, ProductForm.Drink, 1.0, false, null,
                    ServingAmount: ServingIn(block, Unit.Millilitre));

            case NutritionBasis.PerServing:
                if (block.Serving?.Quantity is not { } serving)
                {
                    return null;
                }

                var amount = InBaseUnit(serving);

                // A serving of zero would divide by nothing, and a serving measured in pieces
                // cannot be scaled at all.
                if (amount is null or <= 0)
                {
                    return null;
                }

                var form = FormOf(serving.Unit);

                return new NormalisedPanel(
                    form == ProductForm.Drink ? RuleBasis.Per100ml : RuleBasis.Per100g,
                    form,
                    100.0 / amount.Value,
                    Calculated: true,
                    block.Serving.Description,
                    amount.Value);

            // Per package needs a net weight to scale from, and a package figure with no weight
            // is not comparable with anything. Left out rather than guessed at.
            default:
                return null;
        }
    }

    private static double? ServingIn(NutritionBlock block, Unit expected)
    {
        if (block.Serving?.Quantity is not { } serving)
        {
            return null;
        }

        var amount = InBaseUnit(serving);
        return amount is null ? null : FormOf(serving.Unit) == FormOf(expected) ? amount : null;
    }
}
