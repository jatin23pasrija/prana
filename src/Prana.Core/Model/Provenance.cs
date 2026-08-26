using System.Text.Json.Serialization;

namespace Prana.Core.Model;

/// <summary>
/// One piece of evidence, declared once per record and referenced by <see cref="Id"/> from the
/// provenance map. Declaring sources separately is what keeps a record small: a single
/// photograph of a nutrition panel is referenced once, not repeated for every nutrient.
/// </summary>
public sealed class Source
{
    /// <summary>Record-local identifier, <c>s1</c>, <c>s2</c> and so on. Not globally meaningful.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public required SourceType Type { get; init; }

    /// <summary>Where the evidence lives, when the licence of the source permits recording it.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("retrieved_at")]
    public required string RetrievedAt { get; init; }

    /// <summary>
    /// The licence the source data is under, as recorded in DATA_SOURCES.md. This is what
    /// decides whether the data may be redistributed inside a catalogue package.
    /// </summary>
    [JsonPropertyName("licence")]
    public string? Licence { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// The evidence behind one path in a record. Attached to a path rather than to a value, so a
/// single entry can cover a whole nutrition panel while a disputed field can still be pinned
/// to its own source.
/// </summary>
public sealed class ProvenanceEntry
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("confidence")]
    public required Confidence Confidence { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>One source position in a recorded disagreement.</summary>
public sealed class ConflictValue
{
    [JsonPropertyName("source")]
    public required string Source { get; init; }

    /// <summary>
    /// The value that source reported. Deliberately untyped, because a conflict can be about
    /// a number, a string or a whole ingredient list.
    /// </summary>
    [JsonPropertyName("value")]
    public required System.Text.Json.JsonElement Value { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

/// <summary>
/// A disagreement between sources about the same path. Conflicts are stored rather than
/// averaged away. An unresolved conflict means the record may not be published as verified.
/// </summary>
public sealed class Conflict
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("values")]
    public required IReadOnlyList<ConflictValue> Values { get; init; }

    [JsonPropertyName("resolution")]
    public required ConflictResolution Resolution { get; init; }
}

/// <summary>Whether a record may be shown as trusted, and when that was last established.</summary>
public sealed class Verification
{
    [JsonPropertyName("status")]
    public required VerificationStatus Status { get; init; }

    [JsonPropertyName("last_verified")]
    public required string LastVerified { get; init; }
}
