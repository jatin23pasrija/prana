using Microsoft.Data.Sqlite;
using Prana.Core.Barcodes;
using Prana.Core.Model;

namespace Prana.Data;

/// <summary>Enough about a product to list it, without loading everything.</summary>
/// <param name="Gtin">Canonical key.</param>
/// <param name="Name">As printed on the packet.</param>
/// <param name="Brand">Brand display name, or null.</param>
/// <param name="IsComplete">
/// False when the record has neither nutrition nor ingredients. The app must show these
/// differently and must still offer to look the product up online. See ADR-0026.
/// </param>
public sealed record ProductSummary(string Gtin, string Name, string? Brand, bool IsComplete);

public interface IProductRepository
{
    /// <summary>
    /// Finds a product by anything that identifies it: a scanned barcode, a typed number, a
    /// canonical key. Returns null when the catalogue does not have it, which is the case that
    /// starts online discovery.
    /// </summary>
    Task<ProductRecord?> FindAsync(string barcode, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}

public interface ISearchRepository
{
    Task<IReadOnlyList<ProductSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken);
}

/// <summary>
/// Reads products out of the installed catalogue.
/// </summary>
/// <remarks>
/// A lookup rebuilds the full <see cref="ProductRecord"/>, the same type the repository files and
/// the validator use, rather than a reduced view. The catalogue stores sources and provenance, so
/// there is nothing to invent, and one model everywhere means a screen cannot accidentally show a
/// number without being able to say where it came from.
/// </remarks>
public sealed class ProductRepository(CatalogueConnection catalogue) : IProductRepository
{
    public Task<ProductRecord?> FindAsync(string barcode, CancellationToken cancellationToken)
    {
        // Normalising first is what makes a scan of a UPC-A packet find a record stored under its
        // EAN-13 key. Doing the lookup on the raw digits would miss it.
        if (!Gtin.TryNormalize(barcode, out var gtin) || !catalogue.Exists)
        {
            return Task.FromResult<ProductRecord?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var connection = catalogue.Open();

        var product = ReadProduct(connection, gtin);

        return Task.FromResult(product);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken)
    {
        if (!catalogue.Exists)
        {
            return Task.FromResult(0);
        }

        using var connection = catalogue.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM product";

        return Task.FromResult(Convert.ToInt32(command.ExecuteScalar()));
    }

    private static ProductRecord? ReadProduct(SqliteConnection connection, string gtin)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT p.barcode_printed, p.barcode_format, p.name, p.brand_id, p.category_id,
                   p.package_value, p.package_unit, p.multipack_count, p.ingredients_raw,
                   p.verification_status, p.last_verified
            FROM product p
            WHERE p.gtin = $gtin
            """;

        command.Parameters.AddWithValue("$gtin", gtin);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        var printed = reader.GetString(0);
        var format = Parse<BarcodeFormat>(reader.GetString(1));
        var name = reader.GetString(2);
        var brand = reader.IsDBNull(3) ? null : reader.GetString(3);
        var category = reader.IsDBNull(4) ? null : reader.GetString(4);
        var packageValue = reader.IsDBNull(5) ? (double?)null : reader.GetDouble(5);
        var packageUnit = reader.IsDBNull(6) ? null : reader.GetString(6);
        var multipack = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7);
        var ingredientsRaw = reader.IsDBNull(8) ? null : reader.GetString(8);
        var status = Parse<VerificationStatus>(reader.GetString(9));
        var verified = reader.GetString(10);

        reader.Close();

        return new ProductRecord
        {
            SchemaVersion = 1,
            Gtin = gtin,
            BarcodePrinted = printed,
            BarcodeFormat = format,
            Name = name,
            Brand = brand,
            Category = category,
            Countries = ReadCountries(connection, gtin),
            Package = packageValue is { } value && packageUnit is not null
                ? new PackageInfo
                {
                    Quantity = new Quantity { Value = value, Unit = Parse<Unit>(packageUnit) },
                    MultipackCount = multipack,
                }
                : null,
            Nutrition = ReadNutrition(connection, gtin),
            IngredientsRaw = ingredientsRaw,
            Ingredients = null,
            Sources = ReadSources(connection, gtin),
            Provenance = ReadProvenance(connection, gtin),
            Verification = new Verification { Status = status, LastVerified = verified },
        };
    }

    private static IReadOnlyList<string> ReadCountries(SqliteConnection connection, string gtin)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT country FROM product_country WHERE gtin = $gtin ORDER BY country";
        command.Parameters.AddWithValue("$gtin", gtin);

        var countries = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            countries.Add(reader.GetString(0));
        }

        return countries;
    }

    private static IReadOnlyList<NutritionBlock>? ReadNutrition(SqliteConnection connection, string gtin)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT block_index, basis, serving_description, serving_value, serving_unit,
                   energy_kcal, energy_kj, protein_g, carbohydrate_g, sugars_g, added_sugars_g,
                   fat_g, saturated_fat_g, trans_fat_g, fibre_g, sodium_mg
            FROM nutrition WHERE gtin = $gtin ORDER BY block_index
            """;

        command.Parameters.AddWithValue("$gtin", gtin);

        var blocks = new List<NutritionBlock>();
        var indices = new List<int>();

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                indices.Add(reader.GetInt32(0));

                blocks.Add(new NutritionBlock
                {
                    Basis = Parse<NutritionBasis>(reader.GetString(1)),
                    Serving = reader.IsDBNull(2)
                        ? null
                        : new ServingInfo
                        {
                            Description = reader.GetString(2),
                            Quantity = reader.IsDBNull(3) || reader.IsDBNull(4)
                                ? null
                                : new Quantity
                                {
                                    Value = reader.GetDouble(3),
                                    Unit = Parse<Unit>(reader.GetString(4)),
                                },
                        },
                    Values = new NutritionValues
                    {
                        EnergyKcal = Nullable(reader, 5),
                        EnergyKj = Nullable(reader, 6),
                        ProteinG = Nullable(reader, 7),
                        CarbohydrateG = Nullable(reader, 8),
                        SugarsG = Nullable(reader, 9),
                        AddedSugarsG = Nullable(reader, 10),
                        FatG = Nullable(reader, 11),
                        SaturatedFatG = Nullable(reader, 12),
                        TransFatG = Nullable(reader, 13),
                        FibreG = Nullable(reader, 14),
                        SodiumMg = Nullable(reader, 15),
                    },
                });
            }
        }

        if (blocks.Count == 0)
        {
            return null;
        }

        // not_declared is what separates "the packet does not state it" from "nobody has looked",
        // and dropping it here would make the app show the same thing for both.
        for (var i = 0; i < blocks.Count; i++)
        {
            var notDeclared = ReadNotDeclared(connection, gtin, indices[i]);

            if (notDeclared.Count > 0)
            {
                blocks[i] = new NutritionBlock
                {
                    Basis = blocks[i].Basis,
                    Serving = blocks[i].Serving,
                    Values = blocks[i].Values,
                    NotDeclared = notDeclared,
                };
            }
        }

        return blocks;
    }

    private static IReadOnlyList<string> ReadNotDeclared(SqliteConnection connection, string gtin, int block)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT field FROM nutrition_not_declared WHERE gtin = $gtin AND block_index = $block ORDER BY field";

        command.Parameters.AddWithValue("$gtin", gtin);
        command.Parameters.AddWithValue("$block", block);

        var fields = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            fields.Add(reader.GetString(0));
        }

        return fields;
    }

    private static IReadOnlyList<Source> ReadSources(SqliteConnection connection, string gtin)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT source_id, type, url, retrieved_at, licence FROM source WHERE gtin = $gtin ORDER BY source_id";

        command.Parameters.AddWithValue("$gtin", gtin);

        var sources = new List<Source>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            sources.Add(new Source
            {
                Id = reader.GetString(0),
                Type = Parse<SourceType>(reader.GetString(1)),
                Url = reader.IsDBNull(2) ? null : reader.GetString(2),
                RetrievedAt = reader.GetString(3),
                Licence = reader.IsDBNull(4) ? null : reader.GetString(4),
            });
        }

        return sources;
    }

    private static IReadOnlyDictionary<string, ProvenanceEntry> ReadProvenance(
        SqliteConnection connection,
        string gtin)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT path, source_id, confidence FROM provenance WHERE gtin = $gtin ORDER BY path";

        command.Parameters.AddWithValue("$gtin", gtin);

        var provenance = new Dictionary<string, ProvenanceEntry>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            provenance[reader.GetString(0)] = new ProvenanceEntry
            {
                Source = reader.GetString(1),
                Confidence = Parse<Confidence>(reader.GetString(2)),
            };
        }

        return provenance;
    }

    private static double? Nullable(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);

    /// <summary>
    /// Turns a stored wire name back into its enum, using the same attribute the JSON contract
    /// uses so the catalogue and the record files can never disagree about what a value means.
    /// </summary>
    private static T Parse<T>(string wire)
        where T : struct, Enum
    {
        foreach (var value in Enum.GetValues<T>())
        {
            var member = typeof(T).GetMember(value.ToString())[0];

            var attribute = member
                .GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute), false)
                .Cast<System.Text.Json.Serialization.JsonStringEnumMemberNameAttribute>()
                .FirstOrDefault();

            if (string.Equals(attribute?.Name ?? value.ToString(), wire, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new InvalidDataException($"The catalogue contains '{wire}', which is not a known {typeof(T).Name}.");
    }
}
