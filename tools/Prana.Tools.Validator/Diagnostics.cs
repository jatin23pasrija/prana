namespace Prana.Tools.Validator;

/// <summary>
/// How much a finding matters.
/// </summary>
/// <remarks>
/// Three levels exist because two would force a bad choice. Errors only would mean either
/// blocking a contribution over a rounding artefact, or never mentioning it. Splitting them
/// lets CI block on contradictions while still telling a contributor about the things that are
/// merely worth a second look.
/// </remarks>
public enum Severity
{
    /// <summary>Reported in the summary. Never blocks anything.</summary>
    Info = 0,

    /// <summary>Annotated on the pull request. Does not block, unless --strict is given.</summary>
    Warning = 1,

    /// <summary>Blocks the merge. The record is wrong, not merely suspicious.</summary>
    Error = 2,
}

/// <summary>
/// One finding about one place in one file.
/// </summary>
/// <param name="Severity">How much it matters.</param>
/// <param name="Code">Stable identifier, so a rule can be discussed and suppressed by name.</param>
/// <param name="Message">Plain English. This is what a contributor reads, so it says what is wrong and what to do.</param>
/// <param name="File">Path relative to the repository root, which is what GitHub annotations need.</param>
/// <param name="Pointer">JSON Pointer to the offending value.</param>
/// <param name="Line">1-based line, or 0 when the location is unknown.</param>
/// <param name="Column">1-based column, or 0 when the location is unknown.</param>
public sealed record Diagnostic(
    Severity Severity,
    string Code,
    string Message,
    string File,
    string Pointer = "",
    int Line = 0,
    int Column = 0)
{
    public override string ToString() =>
        Line > 0
            ? $"{File}:{Line}:{Column}: {Severity.ToString().ToLowerInvariant()} {Code}: {Message}"
            : $"{File}: {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
}

/// <summary>
/// Every rule the validator can report, grouped by area.
/// </summary>
/// <remarks>
/// Codes are stable and are never reused for a different meaning. A contributor who reads
/// PRN0401 in a pull request today should find the same rule under that code in a year.
/// The groups are: 01 file and format, 02 schema, 03 identity, 04 nutrition, 05 provenance,
/// 06 ingredients, 07 references, 08 verification.
/// </remarks>
public static class Rules
{
    // File and format
    public const string InvalidJson = "PRN0101";
    public const string NotCanonicalFormat = "PRN0102";
    public const string WrongFileLocation = "PRN0103";
    public const string UnreadableRecord = "PRN0104";

    // Schema
    public const string SchemaViolation = "PRN0201";

    // Identity
    public const string BadCheckDigit = "PRN0301";
    public const string GtinNotCanonical = "PRN0302";
    public const string BarcodeFormatMismatch = "PRN0303";

    // Nutrition
    public const string SaturatedFatExceedsFat = "PRN0401";
    public const string SugarsExceedCarbohydrate = "PRN0402";
    public const string AddedSugarsExceedSugars = "PRN0403";
    public const string TransFatExceedsFat = "PRN0404";
    public const string MacrosExceedBasis = "PRN0405";
    public const string EnergyDisagreesWithMacros = "PRN0406";
    public const string EnergyUnitsDisagree = "PRN0407";
    public const string DuplicateBasis = "PRN0408";
    public const string ServingWithoutMass = "PRN0409";
    public const string NotDeclaredButPresent = "PRN0410";

    // Provenance
    public const string UncoveredValue = "PRN0501";
    public const string UnknownSourceReference = "PRN0502";
    public const string StaleProvenancePath = "PRN0503";
    public const string UnusedSource = "PRN0504";
    public const string VerifiedWithLowConfidence = "PRN0505";
    public const string VerifiedWithUnresolvedConflict = "PRN0506";

    // Ingredients
    public const string ParsedWithoutRaw = "PRN0601";
    public const string UnmatchedIngredient = "PRN0602";
    public const string PercentagesExceedHundred = "PRN0603";

    // References
    public const string DuplicateGtin = "PRN0701";
    public const string UnknownBrand = "PRN0702";
    public const string UnknownCategory = "PRN0703";
    public const string UnknownCountry = "PRN0704";

    // Verification
    public const string VerifiedInTheFuture = "PRN0801";
    public const string StaleVerification = "PRN0802";
}
