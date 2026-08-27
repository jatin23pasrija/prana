using System.Globalization;
using System.Text.Json;

namespace Prana.Tools.Importer.Sources.OpenFoodFacts;

/// <summary>
/// One Open Food Facts product, addressed by field name regardless of which export it came from.
/// </summary>
/// <remarks>
/// The exports do not agree with each other. The tab separated one has around two hundred flat
/// columns; the JSONL one is a nested document with nutrition inside a <c>nutriments</c> object
/// and tag lists as arrays. Putting a single lookup in front of both means the mapper, and every
/// test of it, is written once.
///
/// Unknown names return null rather than throwing. A field disappearing upstream should cost us
/// that one value, not the entire import.
/// </remarks>
public sealed class OffRow(Func<string, string?> lookup)
{
    public string? this[string field]
    {
        get
        {
            var value = lookup(field);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>A row backed by a line of the tab separated export.</summary>
    public static OffRow FromCsv(IReadOnlyDictionary<string, int> columns, string[] values) =>
        new(field => columns.TryGetValue(field, out var index) && index < values.Length
            ? values[index]
            : null);

    /// <summary>
    /// A row backed by one JSONL product document.
    /// </summary>
    /// <remarks>
    /// Nutrition lives in a nested <c>nutriments</c> object rather than in top level fields, so
    /// a name that is not found at the top level is looked for there. Arrays are joined with
    /// commas, which is exactly the shape the flat export uses, so tag matching behaves
    /// identically for both.
    /// </remarks>
    public static OffRow FromJson(JsonElement product)
    {
        var hasNutriments = product.TryGetProperty("nutriments", out var nutriments)
            && nutriments.ValueKind == JsonValueKind.Object;

        return new OffRow(field =>
        {
            if (product.TryGetProperty(field, out var value))
            {
                return Stringify(value);
            }

            return hasNutriments && nutriments.TryGetProperty(field, out var nutrient)
                ? Stringify(nutrient)
                : null;
        });
    }

    private static string? Stringify(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Array => string.Join(
            ',',
            value.EnumerateArray().Select(Stringify).Where(s => !string.IsNullOrEmpty(s))),
        _ => null,
    };

    /// <summary>
    /// Builds a column index from a header line, case-insensitively so a change of case upstream
    /// does not silently drop every field.
    /// </summary>
    public static Dictionary<string, int> IndexHeader(string[] header)
    {
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < header.Length; i++)
        {
            // Duplicate column names exist in some exports. First occurrence wins.
            columns.TryAdd(header[i].Trim(), i);
        }

        return columns;
    }

    /// <summary>Convenience for tests and for the JSONL adapter.</summary>
    public static OffRow FromDictionary(IReadOnlyDictionary<string, string?> fields) =>
        new(field => fields.GetValueOrDefault(field));

    internal static string Format(double value) =>
        value.ToString(CultureInfo.InvariantCulture);
}
