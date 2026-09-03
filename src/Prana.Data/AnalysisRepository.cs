using Microsoft.Data.Sqlite;
using Prana.Core.Model;
using Prana.Core.Rules;

namespace Prana.Data;

/// <summary>
/// One precomputed comparison against the other products in a category.
/// </summary>
/// <param name="PeerCount">How many products the comparison is drawn from. Shown, because a
/// comparison against 31 products and one against 300 look identical otherwise.</param>
public sealed record PeerStat(
    string CategoryId,
    string Nutrient,
    int PeerCount,
    double LowerValue,
    double HigherValue,
    string RuleId,
    string RuleVersion);

public interface IAnalysisRepository
{
    /// <summary>
    /// The ingredient dictionary. Small enough to load whole, and needed in full for any
    /// product, so it is read once and kept.
    /// </summary>
    Task<IReadOnlyList<IngredientRecord>> LoadDictionaryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Peer cut-offs for one category and basis. Empty when the category has too few comparable
    /// products, which is the common case and means the app shows no comparison at all.
    /// </summary>
    Task<IReadOnlyList<PeerStat>> PeerStatsAsync(
        string categoryId,
        NutritionBasis basis,
        CancellationToken cancellationToken);

    /// <summary>
    /// The display name for a brand or category slug, or null when the catalogue has no record
    /// of it.
    /// </summary>
    /// <remarks>
    /// Records store slugs, which is right for data and wrong for a screen. Showing the slug puts
    /// "amul" and "chocolate" in front of someone where the catalogue holds "Amul" and
    /// "Chocolate".
    /// </remarks>
    Task<string?> DisplayNameAsync(string table, string id, CancellationToken cancellationToken);
}

/// <summary>
/// Reads what the analysis needs out of the installed catalogue.
/// </summary>
/// <remarks>
/// The dictionary and the peer statistics ship inside the catalogue rather than the app, so both
/// improve when data improves rather than waiting for a release. The rule files are the opposite:
/// they ship with the app, because a threshold changing is a change to what people were told and
/// should arrive as a version they can see.
/// </remarks>
public sealed class AnalysisRepository(CatalogueConnection catalogue) : IAnalysisRepository
{
    public Task<IReadOnlyList<IngredientRecord>> LoadDictionaryAsync(CancellationToken cancellationToken)
    {
        if (!catalogue.Exists)
        {
            return Task.FromResult<IReadOnlyList<IngredientRecord>>([]);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var connection = catalogue.Open();

        if (!HasTable(connection, "ingredient"))
        {
            return Task.FromResult<IReadOnlyList<IngredientRecord>>([]);
        }

        var aliases = ReadChildren(connection, "SELECT ingredient_id, alias FROM ingredient_alias");
        var flags = ReadChildren(connection, "SELECT ingredient_id, flag FROM ingredient_flag");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, category, explanation FROM ingredient ORDER BY id";

        var results = new List<IngredientRecord>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var id = reader.GetString(0);

            results.Add(new IngredientRecord
            {
                SchemaVersion = 1,
                Id = id,
                Name = reader.GetString(1),
                Category = reader.GetString(2),
                Explanation = reader.IsDBNull(3) ? null : reader.GetString(3),
                Aliases = aliases.GetValueOrDefault(id),
                Flags = flags.GetValueOrDefault(id),

                // The catalogue does not carry the dictionary's own provenance: it is in the
                // repository, and duplicating it into every install would add weight to answer a
                // question the app never asks. These are required by the record type, not used.
                Sources = [],
                Provenance = new Dictionary<string, ProvenanceEntry>(StringComparer.Ordinal),
            });
        }

        return Task.FromResult<IReadOnlyList<IngredientRecord>>(results);
    }

    public Task<IReadOnlyList<PeerStat>> PeerStatsAsync(
        string categoryId,
        NutritionBasis basis,
        CancellationToken cancellationToken)
    {
        // Only the two per-100 bases have peer statistics. A per-serving panel is normalised for
        // display, but it is never ranked against a distribution, because the distribution is
        // built from declared values only.
        var basisName = basis switch
        {
            NutritionBasis.Per100g => "per_100g",
            NutritionBasis.Per100ml => "per_100ml",
            _ => null,
        };

        if (basisName is null || !catalogue.Exists)
        {
            return Task.FromResult<IReadOnlyList<PeerStat>>([]);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var connection = catalogue.Open();

        // A catalogue built before peer statistics existed simply has no table. Offering no
        // comparison is the correct behaviour for it, and is what happens anyway for the 94 per
        // cent of products that have too few peers.
        if (!HasTable(connection, "category_peer_stat"))
        {
            return Task.FromResult<IReadOnlyList<PeerStat>>([]);
        }

        using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT nutrient, peer_count, lower_value, higher_value, rule_id, rule_version
            FROM category_peer_stat
            WHERE category_id = $category AND basis = $basis
            ORDER BY nutrient
            """;

        command.Parameters.AddWithValue("$category", categoryId);
        command.Parameters.AddWithValue("$basis", basisName);

        var results = new List<PeerStat>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new PeerStat(
                categoryId,
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return Task.FromResult<IReadOnlyList<PeerStat>>(results);
    }

    public Task<string?> DisplayNameAsync(string table, string id, CancellationToken cancellationToken)
    {
        // The table name is interpolated, so it is restricted to the two this method exists for
        // rather than trusted from a caller.
        if (table is not ("brand" or "category") || !catalogue.Exists)
        {
            return Task.FromResult<string?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var connection = catalogue.Open();

        if (!HasTable(connection, table))
        {
            return Task.FromResult<string?>(null);
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM {table} WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);

        return Task.FromResult(command.ExecuteScalar() as string);
    }

    /// <summary>
    /// Whether this catalogue carries a table. Catalogues are data files with their own release
    /// cycle, so the app will meet ones built before a feature existed, and the honest response
    /// is to offer less rather than to fail.
    /// </summary>
    private static bool HasTable(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", name);

        return command.ExecuteScalar() is not null;
    }

    private static Dictionary<string, List<string>> ReadChildren(SqliteConnection connection, string sql)
    {
        var results = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var key = reader.GetString(0);

            if (!results.TryGetValue(key, out var list))
            {
                list = [];
                results[key] = list;
            }

            list.Add(reader.GetString(1));
        }

        return results;
    }
}

/// <summary>
/// Where a value sits against the other products in its category.
/// </summary>
public static class PeerComparison
{
    /// <summary>
    /// Describes a value against a peer cut-off, or null when the nutrient is not declared.
    /// </summary>
    /// <remarks>
    /// The wording is deliberately about the shelf rather than about health. If most biscuits in
    /// India carry 30 g of sugar, a biscuit at 30 g is unremarkable among biscuits and still high
    /// by any published measure, and this sentence must not be readable as approval.
    /// </remarks>
    public static string? Describe(PeerStat stat, NutritionValues values, string categoryName)
    {
        var value = Nutrients.Read(values, stat.Nutrient);

        if (value is null)
        {
            return null;
        }

        var where =
            value.Value > stat.HigherValue ? "more than most"
            : value.Value < stat.LowerValue ? "less than most"
            : "about the same as most";

        // The category is named rather than pluralised, because "biscuits" and "chocolate" cannot
        // both be made plural by the same rule and "the 271 biscuits products" was the result of
        // trying.
        return $"{Nutrients.DisplayName(stat.Nutrient)}: {where} of the {stat.PeerCount} products "
               + $"in the {categoryName.ToLowerInvariant()} category that declare a figure.";
    }
}
