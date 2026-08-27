using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prana.Tools.Importer;

/// <summary>
/// What an import did, and more importantly what it refused to do.
/// </summary>
/// <remarks>
/// The dropped counts matter more than the written ones. An import that silently discards eighty
/// percent of a source looks identical to one that works, unless it says so. This report is what
/// turns "the import ran" into something a person can actually judge.
/// </remarks>
public sealed class ImportReport
{
    private readonly Dictionary<string, int> _dropReasons = new(StringComparer.Ordinal);
    private readonly List<string> _rejected = [];

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("retrieved_at")]
    public required string RetrievedAt { get; init; }

    [JsonPropertyName("candidates_read")]
    public int CandidatesRead { get; set; }

    [JsonPropertyName("mapped")]
    public int Mapped { get; set; }

    [JsonPropertyName("dropped")]
    public int Dropped { get; set; }

    [JsonPropertyName("duplicates")]
    public int Duplicates { get; set; }

    /// <summary>Records that mapped cleanly but failed validation, so were never committed.</summary>
    [JsonPropertyName("rejected_by_validator")]
    public int RejectedByValidator { get; set; }

    [JsonPropertyName("written")]
    public int Written { get; set; }

    [JsonPropertyName("unchanged")]
    public int Unchanged { get; set; }

    [JsonPropertyName("drop_reasons")]
    public IReadOnlyDictionary<string, int> DropReasons => _dropReasons;

    /// <summary>A sample of rejected records, capped so a bad run cannot produce a useless wall of text.</summary>
    [JsonPropertyName("rejected_sample")]
    public IReadOnlyList<string> RejectedSample => _rejected;

    public void RecordDrop(string reason)
    {
        Dropped++;
        _dropReasons[reason] = _dropReasons.GetValueOrDefault(reason) + 1;
    }

    public void RecordRejection(string detail)
    {
        RejectedByValidator++;

        if (_rejected.Count < 50)
        {
            _rejected.Add(detail);
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    });

    public string ToSummary()
    {
        var lines = new List<string>
        {
            $"Source            : {Source} (retrieved {RetrievedAt})",
            $"Candidates read   : {CandidatesRead:N0}",
            $"Mapped            : {Mapped:N0}",
            $"Dropped           : {Dropped:N0}",
            $"Duplicate barcodes: {Duplicates:N0}",
            $"Rejected by rules : {RejectedByValidator:N0}",
            $"Written           : {Written:N0}",
            $"Unchanged         : {Unchanged:N0}",
        };

        if (_dropReasons.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Why records were dropped:");
            lines.AddRange(_dropReasons
                .OrderByDescending(p => p.Value)
                .Select(p => $"  {p.Value,10:N0}  {p.Key}"));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
