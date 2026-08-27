using System.Text;
using System.Text.Json;

namespace Prana.Tools.Validator;

/// <summary>
/// Maps every JSON Pointer in a document to the line and column it sits on.
/// </summary>
/// <remarks>
/// Deserialising a record tells you what is wrong. It does not tell you where, and a failure
/// reported as "somewhere in this 200 line file" is a failure a contributor has to hunt for.
/// GitHub can put an annotation on the exact line of a pull request diff, but only if it is
/// given one, so the position has to be recovered while reading.
///
/// For an object member the recorded position is that of the property name rather than the
/// value, because a reviewer looking at a diff wants the annotation on the field, not on the
/// number after the colon.
/// </remarks>
public sealed class JsonLocationMap
{
    private readonly Dictionary<string, (int Line, int Column)> _positions;

    private JsonLocationMap(Dictionary<string, (int, int)> positions) => _positions = positions;

    /// <summary>How many distinct locations were recorded. Used by tests.</summary>
    public int Count => _positions.Count;

    /// <summary>
    /// The position of a pointer, falling back to the nearest recorded ancestor. A rule that
    /// reports a value the document does not contain still lands somewhere useful rather than
    /// on line zero.
    /// </summary>
    public (int Line, int Column) Locate(string pointer)
    {
        var candidate = pointer ?? string.Empty;

        while (true)
        {
            if (_positions.TryGetValue(candidate, out var position))
            {
                return position;
            }

            var slash = candidate.LastIndexOf('/');

            if (slash < 0)
            {
                return _positions.TryGetValue(string.Empty, out var root) ? root : (0, 0);
            }

            candidate = candidate[..slash];
        }
    }

    /// <summary>Reads a document and records where everything in it lives.</summary>
    public static JsonLocationMap Build(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var bytes = Encoding.UTF8.GetBytes(json);
        var lineStarts = BuildLineStarts(bytes);
        var positions = new Dictionary<string, (int, int)>(StringComparer.Ordinal);

        var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });

        var stack = new List<Frame>();
        string? pendingName = null;
        long pendingNameStart = -1;

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    pendingName = reader.GetString();
                    pendingNameStart = reader.TokenStartIndex;
                    break;

                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    {
                        var pointer = NextPointer(stack, pendingName);
                        Record(positions, lineStarts, pointer, pendingNameStart >= 0 ? pendingNameStart : reader.TokenStartIndex);
                        stack.Add(new Frame(pointer, reader.TokenType == JsonTokenType.StartArray));
                        pendingName = null;
                        pendingNameStart = -1;
                        break;
                    }

                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    if (stack.Count > 0)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }

                    break;

                default:
                    {
                        var pointer = NextPointer(stack, pendingName);
                        Record(positions, lineStarts, pointer, pendingNameStart >= 0 ? pendingNameStart : reader.TokenStartIndex);
                        pendingName = null;
                        pendingNameStart = -1;
                        break;
                    }
            }
        }

        return new JsonLocationMap(positions);
    }

    private static string NextPointer(List<Frame> stack, string? pendingName)
    {
        if (stack.Count == 0)
        {
            return string.Empty;
        }

        var frame = stack[^1];

        if (frame.IsArray)
        {
            return $"{frame.Pointer}/{frame.NextIndex++}";
        }

        return $"{frame.Pointer}/{Escape(pendingName ?? string.Empty)}";
    }

    private static void Record(
        Dictionary<string, (int, int)> positions,
        List<int> lineStarts,
        string pointer,
        long byteOffset)
    {
        // The first occurrence wins. A well-formed document has no duplicate pointers, but a
        // malformed one should not make the map throw.
        if (!positions.ContainsKey(pointer))
        {
            positions[pointer] = ToLineColumn(lineStarts, (int)byteOffset);
        }
    }

    private static List<int> BuildLineStarts(byte[] bytes)
    {
        var starts = new List<int> { 0 };

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
            {
                starts.Add(i + 1);
            }
        }

        return starts;
    }

    private static (int Line, int Column) ToLineColumn(List<int> lineStarts, int byteOffset)
    {
        var low = 0;
        var high = lineStarts.Count - 1;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;

            if (lineStarts[mid] <= byteOffset)
            {
                low = mid;
            }
            else
            {
                high = mid - 1;
            }
        }

        // Columns are counted in bytes rather than characters. Record files are almost entirely
        // ASCII, and a column that is slightly off on a line of Devanagari is a far smaller
        // problem than having no position at all.
        return (low + 1, byteOffset - lineStarts[low] + 1);
    }

    /// <summary>JSON Pointer escaping, per RFC 6901.</summary>
    private static string Escape(string segment) =>
        segment.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private sealed class Frame(string pointer, bool isArray)
    {
        public string Pointer { get; } = pointer;

        public bool IsArray { get; } = isArray;

        public int NextIndex { get; set; }
    }
}
