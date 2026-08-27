using System.Globalization;
using Microsoft.Data.Sqlite;
using Prana.Core.Model;

namespace Prana.Tools.CatalogueBuilder;

/// <summary>
/// Writes records into a fresh catalogue database.
/// </summary>
/// <remarks>
/// Everything is written inside one transaction with prepared statements reused across rows.
/// At tens of thousands of products the difference between that and a statement per insert is
/// minutes, not milliseconds.
///
/// Nothing here computes or derives a value. A catalogue is a repackaging of the repository, so
/// a number that appears on a phone must be a number that appears in a record, or the audit
/// trail the whole data policy rests on has a hole in it.
/// </remarks>
public sealed class CatalogueWriter(SqliteConnection connection) : IDisposable
{
    private readonly SqliteTransaction _transaction = connection.BeginTransaction();
    private readonly Dictionary<string, SqliteCommand> _commands = new(StringComparer.Ordinal);

    public int ProductCount { get; private set; }

    public int IncompleteCount { get; private set; }

    public void WriteProduct(ProductRecord product)
    {
        var isComplete = product.Nutrition is { Count: > 0 }
            || !string.IsNullOrEmpty(product.IngredientsRaw);

        Execute(
            """
            INSERT INTO product (gtin, barcode_printed, barcode_format, name, brand_id, category_id,
                                 package_value, package_unit, multipack_count, ingredients_raw,
                                 verification_status, last_verified, is_complete)
            VALUES ($gtin, $printed, $format, $name, $brand, $category,
                    $packageValue, $packageUnit, $multipack, $ingredients,
                    $status, $verified, $complete)
            """,
            ("$gtin", product.Gtin),
            ("$printed", product.BarcodePrinted),
            ("$format", Wire(product.BarcodeFormat)),
            ("$name", product.Name),
            ("$brand", product.Brand),
            ("$category", product.Category),
            ("$packageValue", product.Package?.Quantity.Value),
            ("$packageUnit", product.Package is null ? null : Wire(product.Package.Quantity.Unit)),
            ("$multipack", product.Package?.MultipackCount),
            ("$ingredients", product.IngredientsRaw),
            ("$status", Wire(product.Verification.Status)),
            ("$verified", product.Verification.LastVerified),
            ("$complete", isComplete ? 1 : 0));

        foreach (var country in product.Countries)
        {
            Execute(
                "INSERT OR IGNORE INTO product_country (gtin, country) VALUES ($gtin, $country)",
                ("$gtin", product.Gtin),
                ("$country", country));
        }

        WriteNutrition(product);
        WriteIngredients(product);
        WriteProvenance(product);

        Execute(
            "INSERT INTO product_search (name, brand, gtin) VALUES ($name, $brand, $gtin)",
            ("$name", product.Name),
            ("$brand", product.Brand ?? string.Empty),
            ("$gtin", product.Gtin));

        ProductCount++;

        if (!isComplete)
        {
            IncompleteCount++;
        }
    }

    private void WriteNutrition(ProductRecord product)
    {
        if (product.Nutrition is null)
        {
            return;
        }

        for (var i = 0; i < product.Nutrition.Count; i++)
        {
            var block = product.Nutrition[i];
            var values = block.Values;

            Execute(
                """
                INSERT INTO nutrition (gtin, block_index, basis, serving_description, serving_value,
                                       serving_unit, energy_kcal, energy_kj, protein_g, carbohydrate_g,
                                       sugars_g, added_sugars_g, fat_g, saturated_fat_g, trans_fat_g,
                                       fibre_g, sodium_mg)
                VALUES ($gtin, $index, $basis, $servingDescription, $servingValue, $servingUnit,
                        $kcal, $kj, $protein, $carbohydrate, $sugars, $addedSugars, $fat,
                        $saturatedFat, $transFat, $fibre, $sodium)
                """,
                ("$gtin", product.Gtin),
                ("$index", i),
                ("$basis", Wire(block.Basis)),
                ("$servingDescription", block.Serving?.Description),
                ("$servingValue", block.Serving?.Quantity?.Value),
                ("$servingUnit", block.Serving?.Quantity is { } q ? Wire(q.Unit) : null),
                ("$kcal", values.EnergyKcal),
                ("$kj", values.EnergyKj),
                ("$protein", values.ProteinG),
                ("$carbohydrate", values.CarbohydrateG),
                ("$sugars", values.SugarsG),
                ("$addedSugars", values.AddedSugarsG),
                ("$fat", values.FatG),
                ("$saturatedFat", values.SaturatedFatG),
                ("$transFat", values.TransFatG),
                ("$fibre", values.FibreG),
                ("$sodium", values.SodiumMg));

            foreach (var field in block.NotDeclared ?? [])
            {
                Execute(
                    """
                    INSERT OR IGNORE INTO nutrition_not_declared (gtin, block_index, field)
                    VALUES ($gtin, $index, $field)
                    """,
                    ("$gtin", product.Gtin),
                    ("$index", i),
                    ("$field", field));
            }
        }
    }

    /// <summary>
    /// Flattens the ingredient tree into rows that keep the nesting, using an ordinal and a
    /// parent ordinal. Nothing is written today because no record has a parsed tree yet, but the
    /// shape exists so that filling it later is an insert rather than a migration.
    /// </summary>
    private void WriteIngredients(ProductRecord product)
    {
        if (product.Ingredients is null)
        {
            return;
        }

        var ordinal = 0;
        Walk(product.Ingredients, parent: null);

        void Walk(IReadOnlyList<Ingredient> items, int? parent)
        {
            foreach (var item in items)
            {
                var current = ordinal++;

                Execute(
                    """
                    INSERT INTO ingredient_item (gtin, ordinal, parent_ordinal, raw, canonical_id, percentage)
                    VALUES ($gtin, $ordinal, $parent, $raw, $canonical, $percentage)
                    """,
                    ("$gtin", product.Gtin),
                    ("$ordinal", current),
                    ("$parent", parent),
                    ("$raw", item.Raw),
                    ("$canonical", item.Canonical),
                    ("$percentage", item.Percentage));

                if (item.Children is { Count: > 0 } children)
                {
                    Walk(children, current);
                }
            }
        }
    }

    private void WriteProvenance(ProductRecord product)
    {
        foreach (var source in product.Sources)
        {
            Execute(
                """
                INSERT OR IGNORE INTO source (gtin, source_id, type, url, retrieved_at, licence)
                VALUES ($gtin, $id, $type, $url, $retrieved, $licence)
                """,
                ("$gtin", product.Gtin),
                ("$id", source.Id),
                ("$type", Wire(source.Type)),
                ("$url", source.Url),
                ("$retrieved", source.RetrievedAt),
                ("$licence", source.Licence));
        }

        foreach (var (path, entry) in product.Provenance)
        {
            Execute(
                """
                INSERT OR IGNORE INTO provenance (gtin, path, source_id, confidence)
                VALUES ($gtin, $path, $source, $confidence)
                """,
                ("$gtin", product.Gtin),
                ("$path", path),
                ("$source", entry.Source),
                ("$confidence", Wire(entry.Confidence)));
        }
    }

    public void WriteBrand(BrandRecord brand) => Execute(
        "INSERT OR REPLACE INTO brand (id, name, owner) VALUES ($id, $name, $owner)",
        ("$id", brand.Id),
        ("$name", brand.Name),
        ("$owner", brand.Owner));

    public void WriteCategory(CategoryRecord category)
    {
        Execute(
            """
            INSERT OR REPLACE INTO category (id, name, parent_id, typical_basis)
            VALUES ($id, $name, $parent, $basis)
            """,
            ("$id", category.Id),
            ("$name", category.Name),
            ("$parent", category.Parent),
            ("$basis", Wire(category.TypicalBasis)));

        foreach (var substitute in category.SubstitutableWith ?? [])
        {
            Execute(
                """
                INSERT OR IGNORE INTO category_substitute (category_id, substitute_id)
                VALUES ($id, $substitute)
                """,
                ("$id", category.Id),
                ("$substitute", substitute));
        }

        foreach (var nutrient in category.RelevantNutrients ?? [])
        {
            Execute(
                "INSERT OR IGNORE INTO category_nutrient (category_id, field) VALUES ($id, $field)",
                ("$id", category.Id),
                ("$field", nutrient));
        }
    }

    public void WriteIngredientRecord(IngredientRecord ingredient)
    {
        Execute(
            """
            INSERT OR REPLACE INTO ingredient (id, name, category, explanation)
            VALUES ($id, $name, $category, $explanation)
            """,
            ("$id", ingredient.Id),
            ("$name", ingredient.Name),
            ("$category", ingredient.Category),
            ("$explanation", ingredient.Explanation));

        foreach (var alias in ingredient.Aliases ?? [])
        {
            Execute(
                "INSERT OR IGNORE INTO ingredient_alias (ingredient_id, alias) VALUES ($id, $alias)",
                ("$id", ingredient.Id),
                ("$alias", alias));
        }

        foreach (var flag in ingredient.Flags ?? [])
        {
            Execute(
                "INSERT OR IGNORE INTO ingredient_flag (ingredient_id, flag) VALUES ($id, $flag)",
                ("$id", ingredient.Id),
                ("$flag", flag));
        }
    }

    public void WriteCountry(CountryRecord country) => Execute(
        """
        INSERT OR REPLACE INTO country (code, name, default_basis, sodium_declared_as)
        VALUES ($code, $name, $basis, $sodium)
        """,
        ("$code", country.Code),
        ("$name", country.Name),
        ("$basis", Wire(country.DefaultNutritionBasis)),
        ("$sodium", country.SodiumDeclaredAs));

    public void WriteMeta(string key, string value) => Execute(
        "INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value)",
        ("$key", key),
        ("$value", value));

    public void Commit() => _transaction.Commit();

    /// <summary>
    /// Runs a statement, reusing the prepared command for that SQL. Preparing once per distinct
    /// statement rather than once per row is most of the difference between a build measured in
    /// seconds and one measured in minutes.
    /// </summary>
    private void Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        if (!_commands.TryGetValue(sql, out var command))
        {
            command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = _transaction;

            foreach (var (name, _) in parameters)
            {
                command.Parameters.Add(new SqliteParameter(name, SqliteType.Text));
            }

            command.Prepare();
            _commands[sql] = command;
        }

        foreach (var (name, value) in parameters)
        {
            command.Parameters[name].Value = value ?? DBNull.Value;
        }

        command.ExecuteNonQuery();
    }

    /// <summary>
    /// The wire name for an enum, taken from the same attribute the JSON contract uses, so the
    /// catalogue and the record files can never disagree about what a value is called.
    /// </summary>
    private static string Wire<T>(T value)
        where T : struct, Enum
    {
        var member = typeof(T).GetMember(value.ToString())[0];
        var attribute = member
            .GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute), false)
            .Cast<System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute>()
            .FirstOrDefault();

        return attribute?.Name ?? value.ToString().ToLower(CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        foreach (var command in _commands.Values)
        {
            command.Dispose();
        }

        _transaction.Dispose();
    }
}
