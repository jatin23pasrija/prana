using System.Globalization;
using Prana.Core.Barcodes;
using Prana.Core.Json;
using Prana.Core.Model;
using Prana.Tools.Importer.Sources.OpenFoodFacts;
using Prana.Tools.Validator;

namespace Prana.Tools.Importer;

public sealed class ImportOptions
{
    public required string RepositoryRoot { get; init; }

    public required ISourceAdapter Adapter { get; init; }

    /// <summary>Stop after this many mapped records. Zero means no limit. Used for smoke runs.</summary>
    public int Limit { get; init; }

    /// <summary>Map and report, but write nothing.</summary>
    public bool DryRun { get; init; }

    /// <summary>
    /// Where the JSON Schemas live. Defaults to <c>schema</c> under the repository root, which is
    /// where they are in a checkout; it is settable so a caller with the tree somewhere else does
    /// not have to duplicate them.
    /// </summary>
    public string? SchemaDirectory { get; init; }
}

/// <summary>
/// Maps a source into the repository, then removes anything the data rules reject.
/// </summary>
/// <remarks>
/// Validation runs over the written tree rather than per record, for two reasons. It uses the
/// exact rules CI uses, so a record cannot pass the importer and then fail the build. And rules
/// like duplicate barcode detection only exist across records, so they cannot be evaluated one
/// at a time.
///
/// The importer runs those rules without <c>--strict</c>. Real community data trips warnings
/// constantly, and treating every warning as fatal would reject most of the dataset on day one.
/// Errors are different: an error means the record contradicts itself, and no amount of volume
/// justifies committing that.
/// </remarks>
public sealed class ImportRun(ImportOptions options)
{
    public async Task<ImportReport> ExecuteAsync(TextWriter log, CancellationToken cancellationToken)
    {
        var adapter = options.Adapter;

        var report = new ImportReport
        {
            Source = adapter.Id,
            RetrievedAt = adapter.RetrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        var productsRoot = Path.Combine(options.RepositoryRoot, "data", "products");
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var brands = new BrandCollector(adapter, seen: new Dictionary<string, BrandCollector.Entry>(StringComparer.Ordinal));
        var written = 0;
        var unchanged = 0;

        await foreach (var candidate in adapter.ReadAsync(cancellationToken))
        {
            report.CandidatesRead++;

            if (report.CandidatesRead % 250_000 == 0)
            {
                log.WriteLine($"  read {report.CandidatesRead:N0} rows, mapped {report.Mapped:N0}");
            }

            if (candidate.Product is not { } product)
            {
                report.RecordDrop(candidate.DropReason ?? "unspecified");
                continue;
            }

            // The first record for a barcode wins. Deciding between two rows for the same
            // product is a merge problem, and doing it badly is worse than taking one and
            // recording that the other existed.
            if (!seen.TryAdd(product.Gtin, candidate.SourceId))
            {
                report.Duplicates++;
                continue;
            }

            report.Mapped++;
            brands.Observe(product.Brand, candidate.SourceId);

            if (product.Nutrition is null && string.IsNullOrEmpty(product.IngredientsRaw))
            {
                report.RecordGap(product.Gtin, product.Name);
            }

            if (!options.DryRun)
            {
                if (WriteRecord(productsRoot, product))
                {
                    written++;
                }
                else
                {
                    unchanged++;
                }
            }

            if (options.Limit > 0 && report.Mapped >= options.Limit)
            {
                break;
            }
        }

        if (options.DryRun)
        {
            log.WriteLine("Dry run: nothing was written.");
            return report;
        }

        report.Written = written;
        report.Unchanged = unchanged;

        log.WriteLine(
            $"Wrote {written:N0} product records, left {unchanged:N0} unchanged. "
            + "Generating reference data.");
        brands.Write(options.RepositoryRoot);

        log.WriteLine("Validating what was written.");
        PruneInvalid(productsRoot, report, log);

        return report;
    }

    /// <summary>
    /// Writes a record, but only when something about the product actually changed.
    /// </summary>
    /// <remarks>
    /// The retrieval date is stamped on every record, so a naive comparison finds every record
    /// different on every run: the first monthly re-import rewrote 26,578 files to move two date
    /// lines, added twenty megabytes to the repository, and produced a pull request nobody could
    /// review.
    ///
    /// The dates mattering more than the diff size is the real problem. `last_verified` drives
    /// the freshness thresholds in DATA_POLICY.md, so bumping it on every import means no record
    /// ever becomes stale. A product last touched upstream years ago would report itself current
    /// forever, purely because our importer looked at it again.
    ///
    /// So the comparison ignores those dates, and a record whose substance is unchanged keeps
    /// both its file and its original dates.
    /// </remarks>
    private static bool WriteRecord(string productsRoot, ProductRecord product)
    {
        var directory = Path.Combine(productsRoot, Gtin.ShardFor(product.Gtin));
        var path = Path.Combine(directory, $"{product.Gtin}.json");

        if (File.Exists(path))
        {
            try
            {
                var existing = PranaJson.Deserialize<ProductRecord>(File.ReadAllText(path));

                if (SameSubstance(existing, product))
                {
                    return false;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Unreadable, so it is replaced. Whatever is there is worse than what we have.
            }
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(path, PranaJson.Serialize(product));

        return true;
    }

    /// <summary>
    /// Whether two records say the same thing about the product, ignoring when we looked.
    /// </summary>
    private static bool SameSubstance(ProductRecord existing, ProductRecord candidate) =>
        string.Equals(
            PranaJson.CompactSerialize(WithoutDates(existing)),
            PranaJson.CompactSerialize(WithoutDates(candidate)),
            StringComparison.Ordinal);

    /// <summary>
    /// A copy with every retrieval and verification date blanked, so only real content is
    /// compared. Nothing blanked here is ever written; this exists purely for the comparison.
    /// </summary>
    private static ProductRecord WithoutDates(ProductRecord record) => new()
    {
        SchemaVersion = record.SchemaVersion,
        Gtin = record.Gtin,
        BarcodePrinted = record.BarcodePrinted,
        BarcodeFormat = record.BarcodeFormat,
        Name = record.Name,
        Names = record.Names,
        Brand = record.Brand,
        Category = record.Category,
        Countries = record.Countries,
        Package = record.Package,
        Nutrition = record.Nutrition,
        IngredientsRaw = record.IngredientsRaw,
        Ingredients = record.Ingredients,
        Sources = [.. record.Sources.Select(s => new Source
        {
            Id = s.Id,
            Type = s.Type,
            Url = s.Url,
            RetrievedAt = string.Empty,
            Licence = s.Licence,
            Note = s.Note,
        })],
        Provenance = record.Provenance,
        Conflicts = record.Conflicts,
        Verification = new Verification
        {
            Status = record.Verification.Status,
            LastVerified = string.Empty,
        },
    };

    /// <summary>
    /// Runs the real data rules and deletes anything that produced an error.
    /// </summary>
    private void PruneInvalid(string productsRoot, ImportReport report, TextWriter log)
    {
        var run = new ValidationRun(new ValidationOptions
        {
            RepositoryRoot = options.RepositoryRoot,
            SchemaDirectory = options.SchemaDirectory ?? Path.Combine(options.RepositoryRoot, "schema"),
            Paths = [Path.Combine(options.RepositoryRoot, "data")],
            Strict = false,
        });

        var result = run.Execute();

        var broken = result.Diagnostics
            .Where(d => d.Severity == Severity.Error)
            .GroupBy(d => d.File, StringComparer.Ordinal)
            .ToList();

        foreach (var group in broken)
        {
            var absolute = Path.Combine(options.RepositoryRoot, group.Key.Replace('/', Path.DirectorySeparatorChar));

            // Only ever delete product records. A broken reference record is a bug in this tool
            // and should be fixed, not quietly swept away.
            if (!absolute.StartsWith(productsRoot, StringComparison.OrdinalIgnoreCase))
            {
                log.WriteLine($"  error outside data/products, not pruned: {group.Key}");
                continue;
            }

            var first = group.First();
            report.RecordRejection($"{group.Key}: {first.Code} {first.Message}");

            File.Delete(absolute);
            report.Written--;
        }

        log.WriteLine(
            $"  {result.Diagnostics.Count(d => d.Severity == Severity.Error):N0} error(s) across "
            + $"{broken.Count:N0} file(s), all removed.");
    }
}

/// <summary>
/// Collects the brands products referenced, and writes a record for each.
/// </summary>
/// <remarks>
/// Without these, every imported product would carry a warning saying its brand has no record.
/// The names come from the source rather than being invented, and alternative spellings of the
/// same slug become aliases, which is exactly what the ingredient dictionary does for label
/// wordings.
/// </remarks>
public sealed class BrandCollector(ISourceAdapter adapter, Dictionary<string, BrandCollector.Entry> seen)
{
    public sealed class Entry
    {
        public required string Slug { get; init; }

        public Dictionary<string, int> Spellings { get; } = new(StringComparer.Ordinal);
    }

    public int Count => seen.Count;

    public void Observe(string? slug, string _)
    {
        if (slug is null)
        {
            return;
        }

        if (!seen.TryGetValue(slug, out var entry))
        {
            entry = new Entry { Slug = slug };
            seen[slug] = entry;
        }

        // The slug is all we carry forward from the product, so the display name is rebuilt from
        // it. Keeping a spelling count leaves room to prefer the commonest form later.
        var display = string.Join(' ', slug.Split('-').Select(Capitalise));
        entry.Spellings[display] = entry.Spellings.GetValueOrDefault(display) + 1;
    }

    public void Write(string repositoryRoot)
    {
        var directory = Path.Combine(repositoryRoot, "data", "brands");
        Directory.CreateDirectory(directory);

        foreach (var entry in seen.Values)
        {
            var name = entry.Spellings.OrderByDescending(p => p.Value).First().Key;

            var record = new BrandRecord
            {
                SchemaVersion = 1,
                Id = entry.Slug,
                Name = name,
                Countries = ["IN"],
                Sources =
                [
                    new Source
                    {
                        Id = "s1",
                        Type = SourceType.OpenDatabase,
                        Url = $"https://world.openfoodfacts.org/brand/{entry.Slug}",
                        RetrievedAt = adapter.RetrievedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Licence = adapter.Licence,
                    },
                ],
                Provenance = new Dictionary<string, ProvenanceEntry>(StringComparer.Ordinal)
                {
                    ["name"] = new() { Source = "s1", Confidence = Confidence.Medium },
                },
            };

            var path = Path.Combine(directory, $"{entry.Slug}.json");

            // Same rule as products: a brand whose name has not changed keeps its file and its
            // original retrieval date, so a re-import does not rewrite five thousand records to
            // move one line in each.
            if (File.Exists(path))
            {
                try
                {
                    var existing = PranaJson.Deserialize<BrandRecord>(File.ReadAllText(path));

                    if (string.Equals(existing.Name, record.Name, StringComparison.Ordinal)
                        && string.Equals(existing.Owner, record.Owner, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Unreadable, so it is replaced.
                }
            }

            File.WriteAllText(path, PranaJson.Serialize(record));
        }
    }

    private static string Capitalise(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
