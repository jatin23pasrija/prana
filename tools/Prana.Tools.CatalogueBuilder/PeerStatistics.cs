using Microsoft.Data.Sqlite;
using Prana.Core.Rules;

namespace Prana.Tools.CatalogueBuilder;

/// <summary>
/// Precomputes the cut-offs behind "higher in sugar than most biscuits".
/// </summary>
/// <remarks>
/// Done at build time because the alternative is scanning every product in a category on every
/// product screen, and the screen has a 300 ms budget. It also keeps the answer stable: two
/// people scanning the same biscuit from the same catalogue release see the same comparison.
///
/// The percentiles and the minimum peer count come from the peer_comparison rule file rather
/// than from constants here, so changing how the comparison works stays a reviewable diff
/// against a cited rule, and the version that produced each row is stored beside it.
///
/// Only panels declared on the basis being compared are counted. A per-serving panel is never
/// scaled into the peer set: the app may normalise one panel for display, but a distribution
/// built out of our own arithmetic would quietly become a comparison against our assumptions.
/// </remarks>
public static class PeerStatistics
{
    public static int Compute(SqliteConnection connection, RuleSet rules)
    {
        if (rules.Kind != RuleKind.PeerComparison || rules.PeerComparison is not { } settings)
        {
            throw new ArgumentException("Not a peer comparison rule set.", nameof(rules));
        }

        var groups = Groups(connection, settings.MinimumPeers, settings.Nutrients);
        var written = 0;

        foreach (var (category, basis, nutrient, count) in groups)
        {
            var lower = Percentile(connection, category, basis, nutrient, settings.LowerPercentile, count);
            var higher = Percentile(connection, category, basis, nutrient, settings.HigherPercentile, count);

            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO category_peer_stat
                    (category_id, basis, nutrient, peer_count, lower_value, higher_value,
                     rule_id, rule_version)
                VALUES ($category, $basis, $nutrient, $count, $lower, $higher, $ruleId, $ruleVersion)
                """;

            insert.Parameters.AddWithValue("$category", category);
            insert.Parameters.AddWithValue("$basis", basis);
            insert.Parameters.AddWithValue("$nutrient", nutrient);
            insert.Parameters.AddWithValue("$count", count);
            insert.Parameters.AddWithValue("$lower", lower);
            insert.Parameters.AddWithValue("$higher", higher);
            insert.Parameters.AddWithValue("$ruleId", rules.Id);
            insert.Parameters.AddWithValue("$ruleVersion", rules.Version);
            insert.ExecuteNonQuery();
            written++;
        }

        return written;
    }

    /// <summary>
    /// Every category, basis and nutrient with enough comparable values to rank against.
    /// </summary>
    private static List<(string Category, string Basis, string Nutrient, int Count)> Groups(
        SqliteConnection connection,
        int minimumPeers,
        IReadOnlyList<string> nutrients)
    {
        var results = new List<(string, string, string, int)>();

        foreach (var nutrient in nutrients)
        {
            // The column name is interpolated, so it is checked against the closed set of
            // nutrients the schema allows first. Everything else is a parameter.
            if (!Nutrients.PanelOrder.Contains(nutrient))
            {
                throw new ArgumentException($"Unknown nutrient in rule: {nutrient}", nameof(nutrients));
            }

            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT p.category_id, n.basis, COUNT(*)
                FROM product p
                JOIN nutrition n ON n.gtin = p.gtin
                WHERE p.category_id IS NOT NULL
                  AND n.basis IN ('per_100g', 'per_100ml')
                  AND n.{nutrient} IS NOT NULL
                GROUP BY p.category_id, n.basis
                HAVING COUNT(*) >= $minimum
                """;

            command.Parameters.AddWithValue("$minimum", minimumPeers);

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                results.Add((reader.GetString(0), reader.GetString(1), nutrient, reader.GetInt32(2)));
            }
        }

        return results;
    }

    /// <summary>
    /// The value at a percentile, by position in the sorted values.
    /// </summary>
    /// <remarks>
    /// Nearest-rank rather than interpolated. Interpolating would invent a value that no product
    /// actually has, and while that is defensible in statistics it sits badly beside a rule that
    /// every number shown comes from somewhere. Nearest-rank always returns a real declared value.
    /// </remarks>
    private static double Percentile(
        SqliteConnection connection,
        string category,
        string basis,
        string nutrient,
        double percentile,
        int count)
    {
        var rank = (int)Math.Ceiling(percentile / 100.0 * count);
        var offset = Math.Clamp(rank - 1, 0, count - 1);

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT n.{nutrient}
            FROM product p
            JOIN nutrition n ON n.gtin = p.gtin
            WHERE p.category_id = $category
              AND n.basis = $basis
              AND n.{nutrient} IS NOT NULL
            ORDER BY n.{nutrient}, p.gtin
            LIMIT 1 OFFSET $offset
            """;

        command.Parameters.AddWithValue("$category", category);
        command.Parameters.AddWithValue("$basis", basis);
        command.Parameters.AddWithValue("$offset", offset);

        return Convert.ToDouble(command.ExecuteScalar());
    }
}
