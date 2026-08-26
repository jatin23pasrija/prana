using System.Text.Json;
using Json.Schema;
using Xunit;

namespace Prana.Core.Tests;

/// <summary>
/// Checks the JSON Schemas against real example records. Every example in
/// <c>schema/examples/valid</c> must be accepted and every example in
/// <c>schema/examples/invalid</c> must be rejected, so adding a case is a matter of dropping
/// in a file rather than editing a test.
/// </summary>
public sealed class SchemaTests
{
    /// <summary>
    /// Schemas reference each other by their absolute <c>$id</c>. Registering every file under
    /// that id is what lets those references resolve from disk, with no network access, in
    /// tests and in CI alike. Registration is global and done once.
    /// </summary>
    /// <remarks>
    /// Loading a schema registers it globally under its <c>$id</c>, and registering the same id
    /// twice throws. So every schema is loaded exactly once here and reused, rather than being
    /// read again per test.
    /// </remarks>
    private static readonly Lazy<IReadOnlyDictionary<string, JsonSchema>> Schemas = new(() =>
    {
        var loaded = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(RepositoryPaths.Schemas, "*.schema.json"))
        {
            var name = Path.GetFileName(file).Replace(".schema.json", string.Empty, StringComparison.Ordinal);
            loaded[name] = JsonSchema.FromFile(file);
        }

        return loaded;
    });

    private static EvaluationResults Evaluate(string recordPath)
    {
        var schemaName = RepositoryPaths.SchemaNameFor(recordPath);

        Assert.True(
            Schemas.Value.TryGetValue(schemaName, out var schema),
            $"{Path.GetFileName(recordPath)} implies a schema named {schemaName}, which does not exist.");

        using var instance = JsonDocument.Parse(File.ReadAllText(recordPath));

        return schema!.Evaluate(
            instance.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });
    }

    public static TheoryData<string> ValidExamples() => FileNamesIn(RepositoryPaths.ValidExamples);

    public static TheoryData<string> InvalidExamples() => FileNamesIn(RepositoryPaths.InvalidExamples);

    private static TheoryData<string> FileNamesIn(string directory)
    {
        var data = new TheoryData<string>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ValidExamples))]
    public void Valid_examples_are_accepted(string fileName)
    {
        var result = Evaluate(Path.Combine(RepositoryPaths.ValidExamples, fileName));

        Assert.True(
            result.IsValid,
            $"{fileName} should be valid but the schema rejected it:{Environment.NewLine}{Describe(result)}");
    }

    [Theory]
    [MemberData(nameof(InvalidExamples))]
    public void Invalid_examples_are_rejected(string fileName)
    {
        var result = Evaluate(Path.Combine(RepositoryPaths.InvalidExamples, fileName));

        Assert.False(
            result.IsValid,
            $"{fileName} exists to prove the schema catches a specific mistake, but it was accepted.");
    }

    [Fact]
    public void Every_schema_declares_the_id_that_matches_its_file_name()
    {
        var files = Directory.GetFiles(RepositoryPaths.Schemas, "*.schema.json");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));

            Assert.True(
                document.RootElement.TryGetProperty("$id", out var id),
                $"{Path.GetFileName(file)} has no $id, so other schemas cannot reference it.");

            // A mismatch here would still validate locally but break the moment another schema
            // referenced it by the expected id, which is a confusing failure to debug later.
            Assert.Equal($"https://prana.app/schema/{Path.GetFileName(file)}", id.GetString());
        }
    }

    [Fact]
    public void Every_schema_file_loads_and_registers()
    {
        Assert.Equal(
            Directory.GetFiles(RepositoryPaths.Schemas, "*.schema.json").Length,
            Schemas.Value.Count);
    }

    private static string Describe(EvaluationResults results)
    {
        var lines = Flatten(results)
            .Where(r => r.Errors is { Count: > 0 })
            .SelectMany(r => r.Errors!.Select(e => $"  {r.InstanceLocation} {e.Key}: {e.Value}"))
            .Distinct();

        return string.Join(Environment.NewLine, lines);
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
