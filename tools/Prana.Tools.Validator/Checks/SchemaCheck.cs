using Json.Schema;

namespace Prana.Tools.Validator.Checks;

/// <summary>
/// Validates a record against its JSON Schema and turns the result into located diagnostics.
/// </summary>
/// <remarks>
/// Schemas are loaded once. Loading a schema registers it globally under its <c>$id</c>, and
/// registering the same id twice throws, so the cache is not an optimisation, it is a
/// correctness requirement.
/// </remarks>
public sealed class SchemaCheck
{
    // Loading a schema registers it globally under its $id, and registering the same id twice
    // throws. So this cache is not a speed optimisation: without it, a process that builds two
    // validation runs would crash on the second one.
    private static readonly Dictionary<string, IReadOnlyDictionary<string, JsonSchema>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Lock CacheLock = new();

    private readonly IReadOnlyDictionary<string, JsonSchema> _schemas;

    public SchemaCheck(string schemaDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(schemaDirectory);

        if (!Directory.Exists(schemaDirectory))
        {
            throw new DirectoryNotFoundException($"No schema directory at {schemaDirectory}.");
        }

        _schemas = Load(Path.GetFullPath(schemaDirectory));
    }

    private static IReadOnlyDictionary<string, JsonSchema> Load(string directory)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue(directory, out var cached))
            {
                return cached;
            }

            var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(directory, "*.schema.json"))
            {
                schemas[Path.GetFileName(file)] = JsonSchema.FromFile(file);
            }

            if (schemas.Count == 0)
            {
                throw new InvalidOperationException($"No schemas found in {directory}.");
            }

            Cache[directory] = schemas;
            return schemas;
        }
    }

    public int SchemaCount => _schemas.Count;

    public IEnumerable<Diagnostic> Check(RecordFile file)
    {
        var schemaFile = RecordFile.SchemaFileFor(file.Kind);

        if (schemaFile is null || !_schemas.TryGetValue(schemaFile, out var schema))
        {
            yield break;
        }

        var results = schema.Evaluate(file.Root, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
        });

        if (results.IsValid)
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<(string Pointer, string Keyword, string Message)>();

        foreach (var node in Flatten(results))
        {
            if (node.Errors is not { Count: > 0 })
            {
                continue;
            }

            var pointer = node.InstanceLocation.ToString();
            var evaluationPath = node.EvaluationPath.ToString();

            // A conditional schema reports its failed condition as an error. It is not one: a
            // condition that does not hold simply means the conditional branch does not apply.
            // Surfacing it would tell a contributor their per-100g panel "should be per_serving".
            if (evaluationPath.Contains("/if", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var error in node.Errors)
            {
                var message = Explain(pointer, error.Key, error.Value);

                // A single mistake surfaces once per applicator keyword, and repeating the same
                // sentence three times on one line helps nobody.
                if (seen.Add($"{pointer}|{message}"))
                {
                    found.Add((pointer, error.Key, message));
                }
            }
        }

        // Structural keywords only ever say "something inside here is wrong". When a keyword at
        // the same place says what, the structural restatement is dropped.
        var informative = found
            .Where(f => !StructuralKeywords.Contains(f.Keyword))
            .Select(f => f.Pointer)
            .ToHashSet(StringComparer.Ordinal);

        found.RemoveAll(f => StructuralKeywords.Contains(f.Keyword) && informative.Contains(f.Pointer));

        // A failure deep in the document also fails every object containing it, and those outer
        // failures all say the same unhelpful thing: something below here is wrong. Only the
        // innermost failure names the actual problem, so ancestors are dropped whenever a
        // descendant reported something.
        var pointers = found.Select(f => f.Pointer).ToHashSet(StringComparer.Ordinal);

        foreach (var (pointer, _, message) in found)
        {
            var hasDeeperError = pointers.Any(other =>
                other.Length > pointer.Length
                && other.StartsWith(pointer + "/", StringComparison.Ordinal));

            if (!hasDeeperError)
            {
                yield return file.At(Severity.Error, Rules.SchemaViolation, pointer, message);
            }
        }
    }

    private static readonly HashSet<string> StructuralKeywords = new(StringComparer.Ordinal)
    {
        "allOf", "anyOf", "oneOf", "then", "else", "properties", "items", "prefixItems",
        "additionalProperties", "patternProperties", "propertyNames", "not", "$ref",
    };

    /// <summary>
    /// Turns a schema library message into something a contributor can act on. Most are fine as
    /// they are, but a couple describe the machinery rather than the mistake.
    /// </summary>
    private static string Explain(string pointer, string keyword, string message)
    {
        // additionalProperties: false is evaluated as "this value must match the false schema",
        // which is true and completely unhelpful. What actually happened is a field nobody
        // recognises, which is usually a typo.
        if (message.Contains("false schema", StringComparison.OrdinalIgnoreCase))
        {
            var field = pointer[(pointer.LastIndexOf('/') + 1)..];

            return $"'{field}' is not a field this schema defines. Check the spelling against "
                + "docs/PRODUCT_SCHEMA.md. Adding a new field is a schema change, not a data change.";
        }

        return string.IsNullOrEmpty(keyword) ? message : $"{message} (schema rule: {keyword})";
    }

    private static IEnumerable<EvaluationResults> Flatten(EvaluationResults results)
    {
        yield return results;

        foreach (var child in (results.Details ?? []).SelectMany(Flatten))
        {
            yield return child;
        }
    }
}
