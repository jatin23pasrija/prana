using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prana.Core.Json;

/// <summary>
/// The single JSON contract for every Prana record, on disk and in transit.
/// </summary>
/// <remarks>
/// These settings are not a matter of taste. Records live in Git and are edited by people, so
/// a tool that rewrites a file must produce byte-identical output to what a contributor wrote,
/// or every automated touch would create a noisy diff. That requirement drives all of it:
/// two-space indentation, unescaped non-ASCII so Indian language text stays readable, null
/// properties omitted so an unknown value is genuinely absent rather than written as null, and
/// member declaration order matching the order fields appear in the file.
/// </remarks>
public static class PranaJson
{
    /// <summary>Settings for reading and writing record files.</summary>
    public static readonly JsonSerializerOptions Options = Create(writeIndented: true);

    /// <summary>Compact settings, for hashing, comparison and anything not written to disk.</summary>
    public static readonly JsonSerializerOptions CompactOptions = Create(writeIndented: false);

    /// <summary>
    /// Writer settings for record files. Line endings are forced to LF rather than taking the
    /// platform default, because a record written on Windows and one written on Linux must be
    /// the same bytes. .gitattributes normalises what is committed, but a tool that compares
    /// its own output against a file on disk would still disagree with itself across platforms.
    /// </summary>
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonSerializerOptions Create(bool writeIndented)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            IndentCharacter = ' ',
            IndentSize = 2,

            // An absent property means unknown. Writing null would make "unknown" and
            // "not researched" indistinguishable in the file, which the unknown model in
            // docs/PRODUCT_SCHEMA.md depends on keeping apart.
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,

            // Property names are declared explicitly on every member, so no naming policy is
            // applied here. Being explicit means renaming a C# property can never silently
            // rename a field in tens of thousands of data files.
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = false,

            // A field the schema does not know about is a mistake worth surfacing, not
            // something to discard on read.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,

            // Keeps Devanagari, Gurmukhi and the rest as readable characters rather than
            // \uXXXX escapes. Record files are meant to be read by people in a diff.
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,

            AllowTrailingCommas = false,
            ReadCommentHandling = JsonCommentHandling.Disallow,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>Reads a record from JSON text.</summary>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("Record deserialized to null.");

    /// <summary>
    /// Writes a record exactly as it should appear on disk, including the trailing newline that
    /// every text file in this repository ends with.
    /// </summary>
    public static string Serialize<T>(T value)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            JsonSerializer.Serialize(writer, value, Options);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }
}
