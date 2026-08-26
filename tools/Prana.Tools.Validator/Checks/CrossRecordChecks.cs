namespace Prana.Tools.Validator.Checks;

/// <summary>
/// What every record in the set said about itself, collected so the rules that need the whole
/// tree can run once at the end.
/// </summary>
/// <remarks>
/// Per-file rules stream, because at tens of thousands of records nothing may hold the tree in
/// memory. These rules genuinely cannot: a duplicate barcode is invisible from inside either of
/// the two files that share it. So only the small facts needed for cross-checking are kept, not
/// the records themselves.
/// </remarks>
public sealed class RecordIndex
{
    private readonly Dictionary<string, string> _productsByGtin = new(StringComparer.Ordinal);
    private readonly HashSet<string> _brands = new(StringComparer.Ordinal);
    private readonly HashSet<string> _categories = new(StringComparer.Ordinal);
    private readonly HashSet<string> _ingredients = new(StringComparer.Ordinal);
    private readonly HashSet<string> _countries = new(StringComparer.Ordinal);
    private readonly List<Reference> _references = [];
    private readonly List<Diagnostic> _duplicates = [];

    public int ProductCount => _productsByGtin.Count;

    public void AddProduct(RecordFile file, string gtin)
    {
        if (_productsByGtin.TryGetValue(gtin, out var existing))
        {
            _duplicates.Add(file.At(
                Severity.Error,
                Rules.DuplicateGtin,
                "/gtin",
                $"{gtin} is already used by {existing}. A barcode identifies one product, so one of "
                    + "these is either a duplicate or a misread code."));

            return;
        }

        _productsByGtin[gtin] = file.RelativePath;
    }

    public void AddBrand(string id) => _brands.Add(id);

    public void AddCategory(string id) => _categories.Add(id);

    public void AddIngredient(string id) => _ingredients.Add(id);

    public void AddCountry(string code) => _countries.Add(code);

    public void AddReference(RecordFile file, ReferenceKind kind, string pointer, string value) =>
        _references.Add(new Reference(file, kind, pointer, value));

    /// <summary>
    /// Runs after every file has been read. Reference rules are warnings in Phase 1: making them
    /// errors would mean every product contribution also had to create a brand record, which
    /// would turn a two minute contribution into a chore and cost us the contributions.
    /// </summary>
    public IEnumerable<Diagnostic> Check()
    {
        foreach (var duplicate in _duplicates)
        {
            yield return duplicate;
        }

        foreach (var reference in _references)
        {
            var (known, noun, directory) = reference.Kind switch
            {
                ReferenceKind.Brand => (_brands.Contains(reference.Value), "brand", "brands"),
                ReferenceKind.Category => (_categories.Contains(reference.Value), "category", "categories"),
                ReferenceKind.Country => (_countries.Contains(reference.Value), "country", "countries"),
                _ => (_ingredients.Contains(reference.Value), "ingredient", "ingredients"),
            };

            if (known)
            {
                continue;
            }

            // An unmatched canonical ingredient is explicitly normal, per the schema. Most raw
            // ingredient text has no canonical record yet, and that is a gap to fill rather than
            // a defect to block on.
            if (reference.Kind == ReferenceKind.Ingredient)
            {
                yield return reference.File.At(
                    Severity.Info,
                    Rules.UnmatchedIngredient,
                    reference.Pointer,
                    $"No canonical ingredient record for '{reference.Value}' yet.");

                continue;
            }

            var code = reference.Kind switch
            {
                ReferenceKind.Brand => Rules.UnknownBrand,
                ReferenceKind.Category => Rules.UnknownCategory,
                _ => Rules.UnknownCountry,
            };

            yield return reference.File.At(
                Severity.Warning,
                code,
                reference.Pointer,
                $"There is no {noun} record for '{reference.Value}'. The reference works, but nothing "
                    + $"describes it. Consider adding data/{directory}/{reference.Value}.json.");
        }
    }

    public enum ReferenceKind
    {
        Brand,
        Category,
        Country,
        Ingredient,
    }

    private sealed record Reference(RecordFile File, ReferenceKind Kind, string Pointer, string Value);
}
