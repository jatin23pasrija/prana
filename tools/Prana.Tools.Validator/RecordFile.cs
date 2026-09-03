using System.Text.Json;

namespace Prana.Tools.Validator;

/// <summary>Which schema a file is checked against, taken from where it sits in the tree.</summary>
public enum RecordKind
{
    Unknown,
    Product,
    Ingredient,
    Brand,
    Category,
    Country,
    Alternative,
    Rule,
}

/// <summary>
/// One record file, read once and reused by every rule.
/// </summary>
/// <remarks>
/// Reading is separated from validating so a file is opened a single time no matter how many
/// rules run over it. At tens of thousands of records the difference between one read and eight
/// is the difference between CI feedback in seconds and CI feedback in minutes.
/// </remarks>
public sealed class RecordFile : IDisposable
{
    private readonly Lazy<JsonLocationMap> _locations;

    private RecordFile(
        string absolutePath,
        string relativePath,
        RecordKind kind,
        string text,
        JsonDocument? document,
        Diagnostic? readFailure)
    {
        AbsolutePath = absolutePath;
        RelativePath = relativePath;
        Kind = kind;
        Text = text;
        Document = document;
        ReadFailure = readFailure;
        _locations = new Lazy<JsonLocationMap>(() => JsonLocationMap.Build(Text));
    }

    public string AbsolutePath { get; }

    /// <summary>Path relative to the repository root, which is what annotations must carry.</summary>
    public string RelativePath { get; }

    public RecordKind Kind { get; }

    public string Text { get; }

    /// <summary>Null when the file is not valid JSON. <see cref="ReadFailure"/> then says why.</summary>
    public JsonDocument? Document { get; }

    /// <summary>
    /// Where everything in the file lives. Built on first use rather than on read: most records
    /// produce no findings at all, and mapping every position in a file nobody will annotate is
    /// a whole extra parse per record for nothing.
    /// </summary>
    public JsonLocationMap Locations => _locations.Value;

    /// <summary>Set when the file could not be parsed at all, so later rules are skipped.</summary>
    public Diagnostic? ReadFailure { get; }

    public JsonElement Root => Document!.RootElement;

    public static RecordFile Read(string absolutePath, string repositoryRoot)
    {
        var relative = Path.GetRelativePath(repositoryRoot, absolutePath).Replace('\\', '/');
        var kind = KindFor(relative);
        string text;

        try
        {
            text = File.ReadAllText(absolutePath);
        }
        catch (IOException ex)
        {
            return new RecordFile(absolutePath, relative, kind, string.Empty, null,
                new Diagnostic(Severity.Error, Validator.Rules.UnreadableRecord, ex.Message, relative));
        }

        try
        {
            var document = JsonDocument.Parse(text);
            return new RecordFile(absolutePath, relative, kind, text, document, null);
        }
        catch (JsonException ex)
        {
            // JsonException carries the position, and it is the one message where the position
            // matters most, because everything else about the file is unusable.
            var line = (int)(ex.LineNumber + 1 ?? 0);
            var column = (int)(ex.BytePositionInLine + 1 ?? 0);

            return new RecordFile(absolutePath, relative, kind, text, null,
                new Diagnostic(
                    Severity.Error,
                    Validator.Rules.InvalidJson,
                    $"This file is not valid JSON. {ex.Message}",
                    relative,
                    string.Empty,
                    line,
                    column));
        }
    }

    /// <summary>Builds a diagnostic already pointing at the right place in this file.</summary>
    public Diagnostic At(Severity severity, string code, string pointer, string message)
    {
        var (line, column) = Locations.Locate(pointer);
        return new Diagnostic(severity, code, message, RelativePath, pointer, line, column);
    }

    private static RecordKind KindFor(string relativePath)
    {
        var segments = relativePath.Split('/');

        // Examples are named by schema, real data is filed by directory. Both are validated, so
        // both need to resolve to a schema.
        if (segments.Contains("examples"))
        {
            var name = Path.GetFileNameWithoutExtension(relativePath);
            var dash = name.IndexOf('-');
            return FromName(dash < 0 ? name : name[..dash]);
        }

        // Rule sets live under rules/<area>/, not under data/, because they are not records of
        // the world: they are the thresholds we compare records against. They are validated the
        // same way regardless, since an unschema'd threshold file is how a wrong number reaches
        // the screen.
        if (segments.Length > 1 && segments[0] == "rules")
        {
            return RecordKind.Rule;
        }

        var dataIndex = Array.IndexOf(segments, "data");

        return dataIndex >= 0 && dataIndex + 1 < segments.Length
            ? FromDirectory(segments[dataIndex + 1])
            : RecordKind.Unknown;
    }

    private static RecordKind FromDirectory(string directory) => directory switch
    {
        "products" => RecordKind.Product,
        "ingredients" => RecordKind.Ingredient,
        "brands" => RecordKind.Brand,
        "categories" => RecordKind.Category,
        "countries" => RecordKind.Country,
        "alternatives" => RecordKind.Alternative,
        _ => RecordKind.Unknown,
    };

    private static RecordKind FromName(string name) => name switch
    {
        "product" => RecordKind.Product,
        "ingredient" => RecordKind.Ingredient,
        "brand" => RecordKind.Brand,
        "category" => RecordKind.Category,
        "country" => RecordKind.Country,
        "alternative" => RecordKind.Alternative,
        "rule" => RecordKind.Rule,
        _ => RecordKind.Unknown,
    };

    /// <summary>The schema file name this kind is checked against.</summary>
    public static string? SchemaFileFor(RecordKind kind) => kind switch
    {
        RecordKind.Product => "product.schema.json",
        RecordKind.Ingredient => "ingredient.schema.json",
        RecordKind.Brand => "brand.schema.json",
        RecordKind.Category => "category.schema.json",
        RecordKind.Country => "country.schema.json",
        RecordKind.Alternative => "alternative.schema.json",
        RecordKind.Rule => "rule.schema.json",
        _ => null,
    };

    public void Dispose() => Document?.Dispose();
}
