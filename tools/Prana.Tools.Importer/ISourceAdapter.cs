using Prana.Core.Model;

namespace Prana.Tools.Importer;

/// <summary>
/// One record offered by a source, either mapped into our shape or explained away.
/// </summary>
/// <param name="SourceId">The identifier the source uses, kept for the drop report.</param>
/// <param name="Product">The mapped record, or null when the candidate was rejected.</param>
/// <param name="DropReason">Why it was rejected. Required whenever <paramref name="Product"/> is null.</param>
public sealed record ImportCandidate(string SourceId, ProductRecord? Product, string? DropReason)
{
    public static ImportCandidate Accepted(string sourceId, ProductRecord product) =>
        new(sourceId, product, null);

    public static ImportCandidate Dropped(string sourceId, string reason) =>
        new(sourceId, null, reason);
}

/// <summary>
/// Reads products from one external source and maps them into Prana records.
/// </summary>
/// <remarks>
/// Every source is a separate adapter so that adding one is an addition rather than a change.
/// The interface deliberately carries the licence and attribution: a source that cannot state
/// them has not been through the review in DATA_SOURCES.md, and must not be importable.
/// </remarks>
public interface ISourceAdapter
{
    /// <summary>Stable identifier, matching the directory under <c>sources/</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>The licence the source data is under, recorded on every record it produces.</summary>
    string Licence { get; }

    /// <summary>The exact attribution wording the licence requires.</summary>
    string Attribution { get; }

    /// <summary>The date the underlying data was retrieved, stamped on every record.</summary>
    DateOnly RetrievedAt { get; }

    /// <summary>
    /// Streams candidates. Implementations must stream rather than materialise: a source dump
    /// can be many gigabytes, and nothing in this pipeline may depend on it fitting in memory.
    /// </summary>
    IAsyncEnumerable<ImportCandidate> ReadAsync(CancellationToken cancellationToken);
}
