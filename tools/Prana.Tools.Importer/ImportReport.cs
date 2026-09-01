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
    private readonly List<Gap> _gaps = [];

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

    /// <summary>
    /// Records kept despite having neither nutrition nor ingredients. These are the catalogue's
    /// known gaps, and the reason they are counted separately is that they are the work queue
    /// for the research automation rather than a failure.
    /// </summary>
    [JsonPropertyName("incomplete")]
    public int Incomplete { get; set; }

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

    public void RecordGap(string gtin, string name)
    {
        Incomplete++;
        _gaps.Add(new Gap(gtin, name));
    }

    /// <summary>
    /// Written to its own file rather than into the report. There can be tens of thousands of
    /// these, and burying the summary under them would make the report unreadable for the
    /// person who has to decide whether to merge.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<Gap> Gaps => _gaps;

    /// <summary>The gap queue, as a document the research automation can consume directly.</summary>
    public string GapsToJson() => JsonSerializer.Serialize(
        new
        {
            source = Source,
            retrieved_at = RetrievedAt,
            count = _gaps.Count,
            note = "Products known to exist with neither nutrition nor ingredients. "
                + "This is the seed work queue for the research automation in F14.",
            products = _gaps.OrderBy(g => g.Gtin, StringComparer.Ordinal),
        },
        new JsonSerializerOptions { WriteIndented = true }) + "\n";

    /// <summary>One product we know exists and know nothing else about.</summary>
    public sealed record Gap(
        [property: JsonPropertyName("gtin")] string Gtin,
        [property: JsonPropertyName("name")] string Name);

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
            $"Written           : {Written:N0}   (new, or changed since last import)",
            $"Unchanged         : {Unchanged:N0}   (left alone, dates kept)",
            $"Incomplete        : {Incomplete:N0}   (of those mapped: no nutrition and no ingredients)",
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
