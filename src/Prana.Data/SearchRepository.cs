using System.Text;

namespace Prana.Data;

/// <summary>
/// Full text search over product name and brand.
/// </summary>
/// <remarks>
/// The query is rewritten rather than passed through. FTS5 has its own syntax, so a shopper
/// typing an apostrophe, a hyphen or the word AND would otherwise get a syntax error instead of
/// results. Every term is quoted and given a prefix match, which is what makes search feel
/// responsive as someone types rather than only once they finish a word.
/// </remarks>
public sealed class SearchRepository(CatalogueConnection catalogue) : ISearchRepository
{
    public Task<IReadOnlyList<ProductSummary>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken)
    {
        var expression = ToMatchExpression(query);

        if (expression is null || !catalogue.Exists)
        {
            return Task.FromResult<IReadOnlyList<ProductSummary>>([]);
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var connection = catalogue.Open();
        using var command = connection.CreateCommand();

        // Joined back to product for is_complete, which the app needs to decide whether to offer
        // discovery, and to brand for a display name rather than a slug.
        command.CommandText =
            """
            SELECT p.gtin, p.name, b.name, p.is_complete
            FROM product_search s
            JOIN product p ON p.gtin = s.gtin
            LEFT JOIN brand b ON b.id = p.brand_id
            WHERE product_search MATCH $query
            ORDER BY bm25(product_search), p.is_complete DESC
            LIMIT $limit
            """;

        command.Parameters.AddWithValue("$query", expression);
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<ProductSummary>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            results.Add(new ProductSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3) == 1));
        }

        return Task.FromResult<IReadOnlyList<ProductSummary>>(results);
    }

    /// <summary>
    /// Turns what someone typed into an FTS5 expression, or null when there is nothing to search
    /// for. Quoting each term makes punctuation and FTS5 keywords literal instead of syntax.
    /// </summary>
    internal static string? ToMatchExpression(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var builder = new StringBuilder();

        foreach (var term in query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Keep letters and digits. Everything else is either punctuation the tokeniser drops
            // anyway, or a character that would change the meaning of the expression.
            var cleaned = new string([.. term.Where(char.IsLetterOrDigit)]);

            if (cleaned.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            // Prefix matching, so results appear while someone is still typing.
            builder.Append('"').Append(cleaned).Append("\"*");
        }

        return builder.Length == 0 ? null : builder.ToString();
    }
}
