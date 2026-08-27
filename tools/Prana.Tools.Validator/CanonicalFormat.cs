using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Prana.Tools.Validator;

/// <summary>
/// Rewrites a record into the one format the repository accepts.
/// </summary>
/// <remarks>
/// Formatting is done through JsonDocument rather than through the typed model on purpose. A
/// contributor whose file has a misspelled field still deserves to have their indentation fixed,
/// and reformatting must never depend on the record being valid. JsonElement preserves the
/// original text of every number, so 18 stays 18 and 13.10 does not silently become 13.1.
///
/// This exists because CI failing someone over two spaces is hostile and teaches nothing. The
/// right answer is a command that fixes it.
/// </remarks>
public static class CanonicalFormat
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Returns the canonical text for a document, including the trailing newline.</summary>
    public static string Format(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            document.RootElement.WriteTo(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    /// <summary>
    /// Whether a file is already canonical. Line endings are normalised first, because Git
    /// decides what is on disk and that answer differs by platform.
    /// </summary>
    public static bool IsCanonical(string json, out string canonical)
    {
        canonical = Format(json);
        return string.Equals(Normalise(json), canonical, StringComparison.Ordinal);
    }

    private static string Normalise(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
