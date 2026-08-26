using System.Text;
using System.Text.Json;

namespace Prana.Tools.Validator;

/// <summary>
/// Translation between the two ways this project names a place inside a record.
/// </summary>
/// <remarks>
/// Provenance paths are written the way a person would say them, <c>nutrition[0].values.sugars_g</c>,
/// because contributors type them by hand. Everything mechanical, from JSON Schema errors to
/// annotations, uses JSON Pointer, <c>/nutrition/0/values/sugars_g</c>. One conversion in one
/// place stops the two notations from drifting apart.
/// </remarks>
public static class Pointers
{
    /// <summary>Converts a dotted provenance path into a JSON Pointer.</summary>
    public static string FromProvenancePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (path.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var segment = new StringBuilder();

        foreach (var c in path)
        {
            switch (c)
            {
                case '.':
                    Flush(builder, segment);
                    break;

                case '[':
                    Flush(builder, segment);
                    break;

                case ']':
                    Flush(builder, segment);
                    break;

                default:
                    segment.Append(c);
                    break;
            }
        }

        Flush(builder, segment);
        return builder.ToString();

        static void Flush(StringBuilder builder, StringBuilder segment)
        {
            if (segment.Length == 0)
            {
                return;
            }

            builder.Append('/').Append(Escape(segment.ToString()));
            segment.Clear();
        }
    }

    /// <summary>
    /// Whether <paramref name="ancestor"/> covers <paramref name="pointer"/>. This is the whole
    /// of the coverage rule in ADR-0018: a path backs itself and everything beneath it.
    /// </summary>
    public static bool Covers(string ancestor, string pointer)
    {
        if (ancestor.Length == 0)
        {
            return true;
        }

        if (string.Equals(ancestor, pointer, StringComparison.Ordinal))
        {
            return true;
        }

        // The trailing slash matters. Without it "/name" would appear to cover "/names".
        return pointer.StartsWith(ancestor + "/", StringComparison.Ordinal);
    }

    /// <summary>Resolves a pointer against a document, so a rule can tell a stale path from a real one.</summary>
    public static bool TryResolve(JsonElement root, string pointer, out JsonElement value)
    {
        value = root;

        if (pointer.Length == 0)
        {
            return true;
        }

        foreach (var raw in pointer.TrimStart('/').Split('/'))
        {
            var segment = Unescape(raw);

            switch (value.ValueKind)
            {
                case JsonValueKind.Object when value.TryGetProperty(segment, out var property):
                    value = property;
                    break;

                case JsonValueKind.Array when int.TryParse(segment, out var index)
                    && index >= 0 && index < value.GetArrayLength():
                    value = value[index];
                    break;

                default:
                    return false;
            }
        }

        return true;
    }

    private static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static string Unescape(string segment) =>
        segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
}
