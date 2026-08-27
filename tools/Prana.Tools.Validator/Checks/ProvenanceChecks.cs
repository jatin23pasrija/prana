using System.Text.Json;
using Prana.Core.Model;

namespace Prana.Tools.Validator.Checks;

/// <summary>
/// Enforces ADR-0018. Without this, the provenance map is documentation rather than a rule, and
/// a value with no evidence behind it would merge unnoticed.
/// </summary>
public static class ProvenanceChecks
{
    /// <summary>
    /// The claims a product makes about the thing in the packet. Each of these needs evidence
    /// when it is present.
    /// </summary>
    private static readonly string[] ProductClaims =
    [
        "/name",
        "/names",
        "/brand",
        "/category",
        "/package",
        "/ingredients_raw",
        "/ingredients",
    ];

    /// <summary>
    /// Fields deliberately exempt, per ADR-0017 and the F02 discussion. These are structural or
    /// derived rather than claims about the product: the key, the barcode it came from, the
    /// schema version, and the verification block that records the outcome of checking. When one
    /// of them looks wrong the app asks the user to raise a correction, which is a better answer
    /// than inventing a source for a field nobody sourced.
    /// </summary>
    private static readonly string[] Exempt =
    [
        "/schema_version",
        "/gtin",
        "/barcode_printed",
        "/barcode_format",
        "/countries",
        "/verification",
        "/conflicts",
        "/sources",
        "/provenance",
    ];

    public static IEnumerable<Diagnostic> Check(
        RecordFile file,
        IReadOnlyDictionary<string, ProvenanceEntry> provenance,
        IReadOnlyList<Source> sources,
        IEnumerable<string> requiredPointers,
        Verification? verification,
        IReadOnlyList<Conflict>? conflicts)
    {
        var sourceIds = sources.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        var covered = new List<string>();

        foreach (var (path, entry) in provenance)
        {
            var pointer = Pointers.FromProvenancePath(path);
            covered.Add(pointer);
            referenced.Add(entry.Source);

            if (!sourceIds.Contains(entry.Source))
            {
                yield return file.At(
                    Severity.Error,
                    Rules.UnknownSourceReference,
                    $"/provenance/{path}",
                    $"This points at source '{entry.Source}', which is not declared in sources.");
            }

            if (!Pointers.TryResolve(file.Root, pointer, out _))
            {
                yield return file.At(
                    Severity.Warning,
                    Rules.StaleProvenancePath,
                    $"/provenance/{path}",
                    $"'{path}' does not exist in this record. It is probably left over from a field "
                        + "that was removed, and it now backs nothing.");
            }
        }

        foreach (var required in requiredPointers)
        {
            if (covered.Any(c => Pointers.Covers(c, required)))
            {
                continue;
            }

            yield return file.At(
                Severity.Error,
                Rules.UncoveredValue,
                required,
                $"Nothing in the provenance map covers '{required}'. Every published value needs a "
                    + "source. Add an entry for this path, or for any path above it.");
        }

        foreach (var source in sources)
        {
            if (!referenced.Contains(source.Id))
            {
                yield return file.At(
                    Severity.Info,
                    Rules.UnusedSource,
                    "/sources",
                    $"Source '{source.Id}' is declared but nothing references it.");
            }
        }

        if (verification?.Status != VerificationStatus.Verified)
        {
            yield break;
        }

        foreach (var (path, entry) in provenance)
        {
            if (entry.Confidence == Confidence.Low)
            {
                yield return file.At(
                    Severity.Error,
                    Rules.VerifiedWithLowConfidence,
                    $"/provenance/{path}",
                    "A verified record cannot rest on low-confidence evidence. Either strengthen the "
                        + "evidence or set verification.status to unverified.");
            }
        }

        if (conflicts?.Any(c => c.Resolution == ConflictResolution.Unresolved) == true)
        {
            yield return file.At(
                Severity.Error,
                Rules.VerifiedWithUnresolvedConflict,
                "/verification/status",
                "This record has an unresolved conflict, so it cannot be verified. Set the status to "
                    + "disputed until the sources are reconciled.");
        }
    }

    /// <summary>
    /// Which pointers in this particular product need evidence. Only claims that are actually
    /// present are required, so a sparse record is not punished for what it does not yet say.
    /// </summary>
    public static IEnumerable<string> RequiredPointersFor(RecordFile file, ProductRecord product)
    {
        foreach (var claim in ProductClaims)
        {
            if (!Pointers.TryResolve(file.Root, claim, out var value))
            {
                continue;
            }

            // An empty translations map or ingredient list claims nothing, so it needs no source.
            if (IsEmpty(value))
            {
                continue;
            }

            yield return claim;
        }

        if (product.Nutrition is null)
        {
            yield break;
        }

        // Each panel is required separately. One photograph usually covers them all through a
        // single "nutrition" entry, but a panel taken from a different source must say so.
        for (var i = 0; i < product.Nutrition.Count; i++)
        {
            yield return $"/nutrition/{i}";
        }
    }

    /// <summary>The pointers on other record types that need evidence.</summary>
    public static IEnumerable<string> RequiredPointersFor(RecordFile file, params string[] claims)
    {
        foreach (var claim in claims)
        {
            if (Pointers.TryResolve(file.Root, claim, out var value) && !IsEmpty(value))
            {
                yield return claim;
            }
        }
    }

    /// <summary>Fields that never need evidence, exposed so the rule set can be documented and tested.</summary>
    public static IReadOnlyList<string> ExemptPointers => Exempt;

    private static bool IsEmpty(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => true,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.String => value.GetString() is null or "",
        _ => false,
    };
}
