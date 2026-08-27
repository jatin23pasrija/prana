using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prana.Tools.Importer;

/// <summary>
/// A record of exactly which upstream file an import was built from.
/// </summary>
/// <remarks>
/// The dump itself is around 1.2 GB and changes daily, so it is never committed. But an import
/// nobody can trace back to a specific upstream state is an import nobody can audit, and the
/// whole data policy rests on being able to say where a value came from.
///
/// So the snapshot manifest is committed instead: the URL, the date, the size and the checksum
/// of the file that produced the records. It is small, it is human-readable, and it is enough to
/// tell whether two imports used the same input.
/// </remarks>
public sealed class Snapshot
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("url")]
    public required string Url { get; init; }

    [JsonPropertyName("retrieved_at")]
    public required string RetrievedAt { get; init; }

    /// <summary>Null when the export was streamed and never written to disk.</summary>
    [JsonPropertyName("size_bytes")]
    public long? SizeBytes { get; init; }

    /// <summary>Null when the export was streamed and never written to disk.</summary>
    [JsonPropertyName("sha256")]
    public string? Sha256 { get; init; }

    [JsonPropertyName("licence")]
    public required string Licence { get; init; }

    [JsonPropertyName("attribution")]
    public required string Attribution { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }

    public static Snapshot Describe(string path, ISourceAdapter adapter, string url, string? note = null)
    {
        var info = new FileInfo(path);

        using var stream = File.OpenRead(path);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));

        return new Snapshot
        {
            Source = adapter.Id,
            Url = url,
            RetrievedAt = adapter.RetrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SizeBytes = info.Length,
            Sha256 = hash,
            Licence = adapter.Licence,
            Attribution = adapter.Attribution,
            Note = note,
        };
    }

    /// <summary>
    /// Describes an export that was streamed rather than stored, so there is nothing to
    /// checksum. Recording the url and date without a hash is honest. Inventing one would not be.
    /// </summary>
    public static Snapshot Streamed(ISourceAdapter adapter, string url, string? note = null) =>
        new()
        {
            Source = adapter.Id,
            Url = url,
            RetrievedAt = adapter.RetrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SizeBytes = null,
            Sha256 = null,
            Licence = adapter.Licence,
            Attribution = adapter.Attribution,
            Note = note is null
                ? "Streamed from the network, so the export was never on disk to checksum."
                : note + " Streamed from the network, so the export was never on disk to checksum.",
        };

    public string ToJson() =>
        JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true,
            IndentCharacter = ' ',
            IndentSize = 2,
            NewLine = "\n",
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        }) + "\n";
}
