namespace Prana.Tools.Importer.Sources.OpenFoodFacts;

/// <summary>
/// Maps an Open Food Facts category tag onto one of ours.
/// </summary>
/// <remarks>
/// The Open Food Facts taxonomy has thousands of categories. Importing it wholesale would give
/// us thousands of categories that mean nothing to the alternatives engine, which only needs to
/// answer one question: is this a reasonable substitute for that.
///
/// So the mapping is deliberately small and curated around Indian packaged food. A product
/// whose tags match nothing is left with no category at all, which is honest. It means no
/// alternatives are offered for it rather than bad ones, and it leaves a visible gap for
/// someone to fill.
///
/// The most specific match wins, so a cream biscuit is not filed as a generic biscuit.
/// </remarks>
public static class CategoryMap
{
    /// <summary>
    /// Open Food Facts tag to Prana category, most specific first. Order is the priority.
    /// </summary>
    private static readonly (string Tag, string Category)[] Mappings =
    [
        // Biscuits and bakery
        ("en:cream-biscuits", "cream-biscuits"),
        ("en:marie-biscuits", "marie-biscuits"),
        ("en:digestive-biscuits", "digestive-biscuits"),
        ("en:dry-biscuits", "biscuits"),
        ("en:crackers", "crackers"),
        ("en:cookies", "cookies"),
        ("en:biscuits-and-crackers", "biscuits"),
        ("en:biscuits", "biscuits"),
        ("en:cakes", "cakes"),
        ("en:rusks", "rusk"),
        ("en:breads", "bread"),

        // Savoury snacks
        ("en:chips-and-fries", "chips"),
        ("en:crisps", "chips"),
        ("en:extruded-snacks", "extruded-snacks"),
        ("en:namkeen", "namkeen"),
        ("en:salty-snacks", "namkeen"),

        // Confectionery
        ("en:chocolates", "chocolate"),
        ("en:chocolate-bars", "chocolate"),
        ("en:candies", "candy"),
        ("en:sweets", "candy"),

        // Dairy and frozen
        ("en:ice-creams", "ice-cream"),
        ("en:yogurts", "yogurt"),
        ("en:cheeses", "cheese"),
        ("en:butters", "butter"),
        ("en:ghee", "ghee"),
        ("en:milks", "milk"),
        ("en:milk-powders", "milk-powder"),

        // Staples
        ("en:vegetable-oils", "edible-oil"),
        ("en:olive-oils", "edible-oil"),
        ("en:flours", "flour"),
        ("en:wheat-flours", "flour"),
        ("en:rices", "rice"),
        ("en:legumes", "pulses"),
        ("en:lentils", "pulses"),
        ("en:pasta", "pasta"),
        ("en:noodles", "noodles"),
        ("en:instant-noodles", "noodles"),
        ("en:breakfast-cereals", "breakfast-cereal"),
        ("en:oatmeals", "oats"),
        ("en:sugars", "sugar"),
        ("en:salts", "salt"),
        ("en:spices", "spices"),
        ("en:masalas", "spices"),

        // Drinks
        ("en:teas", "tea"),
        ("en:coffees", "coffee"),
        ("en:sodas", "soft-drink"),
        ("en:energy-drinks", "energy-drink"),
        ("en:fruit-juices", "fruit-juice"),
        ("en:fruit-based-beverages", "fruit-drink"),
        ("en:waters", "water"),

        // Spreads and condiments
        ("en:jams", "jam"),
        ("en:honeys", "honey"),
        ("en:peanut-butters", "nut-butter"),
        ("en:ketchup", "ketchup"),
        ("en:sauces", "sauce"),
        ("en:pickles", "pickle"),

        // Other
        ("en:baby-foods", "baby-food"),
        ("en:protein-powders", "protein-supplement"),
        ("en:nuts", "nuts"),
        ("en:dried-fruits", "dried-fruit"),
        ("en:ready-made-meals", "ready-to-eat"),
        ("en:soups", "soup"),
    ];

    /// <summary>Every category this mapping can produce, so records can be generated for them.</summary>
    public static IReadOnlySet<string> KnownCategories { get; } =
        Mappings.Select(m => m.Category).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Picks a category from a tag list. Accepts either the comma-joined form used by the bulk
    /// export or a single tag.
    /// </summary>
    public static string? Map(string? categoryTags)
    {
        if (string.IsNullOrWhiteSpace(categoryTags))
        {
            return null;
        }

        var tags = categoryTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Walk our list rather than theirs, so the first match is the most specific one we
        // recognise rather than whichever tag they happened to list first.
        foreach (var (tag, category) in Mappings)
        {
            foreach (var candidate in tags)
            {
                if (string.Equals(candidate, tag, StringComparison.OrdinalIgnoreCase))
                {
                    return category;
                }
            }
        }

        return null;
    }
}
