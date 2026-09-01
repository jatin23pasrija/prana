namespace Prana.Core.Barcodes;

/// <summary>What a scan turned out to be.</summary>
public enum ScanKind
{
    /// <summary>A product barcode. <see cref="ScannedCode.Gtin"/> holds the canonical key.</summary>
    Product,

    /// <summary>Readable, but not a product code: a website, a payment code, a serial number.</summary>
    NotAProduct,

    /// <summary>Digits in the right shape whose check digit does not hold up.</summary>
    BadCheckDigit,

    /// <summary>Nothing usable.</summary>
    Empty,
}

/// <summary>The result of reading one code.</summary>
/// <param name="Kind">What it turned out to be.</param>
/// <param name="Gtin">The canonical key, set only when <see cref="Kind"/> is <see cref="ScanKind.Product"/>.</param>
/// <param name="Raw">Exactly what the decoder returned, kept for diagnosis and for the message shown.</param>
public sealed record ScannedCode(ScanKind Kind, string? Gtin, string Raw);

/// <summary>
/// Turns whatever a barcode decoder produced into a product key, or says why it could not.
/// </summary>
/// <remarks>
/// The scanner accepts a wide set of symbologies, which means people will point it at QR codes,
/// payment codes and warranty labels. Those have to fail with an explanation rather than a
/// silent nothing, and that is most of what this class is for.
///
/// It also handles GS1 element strings. Indian packaging increasingly carries GS1 DataMatrix or
/// GS1-128, where the GTIN is not the whole payload but one field inside it, introduced by
/// application identifier 01. Reading those is the main reason enabling more symbologies is
/// worth anything: without this, a GS1 DataMatrix decodes fine and then looks like a failure.
/// </remarks>
public static class BarcodeReader
{
    /// <summary>The GS1 application identifier for a GTIN.</summary>
    private const string GtinIdentifier = "01";

    /// <summary>A GTIN element string is the identifier plus exactly 14 digits.</summary>
    private const int GtinElementLength = 14;

    /// <summary>
    /// Group separator, ASCII 29. GS1 uses it to end a variable-length field, and decoders
    /// commonly hand it back inside the string.
    /// </summary>
    private const char GroupSeparator = '';

    public static ScannedCode Read(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new ScannedCode(ScanKind.Empty, null, string.Empty);
        }

        var text = raw.Trim();

        // The common case: the whole payload is the barcode number.
        if (Gtin.TryNormalize(text, out var direct))
        {
            return new ScannedCode(ScanKind.Product, direct, text);
        }

        if (TryReadGs1(text, out var fromGs1))
        {
            return new ScannedCode(ScanKind.Product, fromGs1, text);
        }

        // Digits of a plausible length that failed the check digit. Worth separating from a QR
        // code, because it means aim again rather than this is not a product.
        var digitsOnly = text.All(char.IsAsciiDigit);

        return digitsOnly && text.Length is >= 8 and <= 14
            ? new ScannedCode(ScanKind.BadCheckDigit, null, text)
            : new ScannedCode(ScanKind.NotAProduct, null, text);
    }

    /// <summary>
    /// Pulls a GTIN out of a GS1 element string.
    /// </summary>
    /// <remarks>
    /// Only application identifier 01 is read, and only when it is fixed length, which it always
    /// is. Everything after it is ignored: batch numbers and expiry dates are on the packet, and
    /// parsing the full GS1 grammar to reach a field we do not use would be a lot of code that
    /// could get a product wrong.
    /// </remarks>
    private static bool TryReadGs1(string text, out string? gtin)
    {
        gtin = null;

        // Human-readable form, as printed under the symbol: (01)08901234567890
        var normalised = text
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(GroupSeparator.ToString(), string.Empty, StringComparison.Ordinal);

        // Some decoders prefix a symbology identifier such as ]d2 or ]C1.
        if (normalised.StartsWith(']') && normalised.Length > 3)
        {
            normalised = normalised[3..];
        }

        if (!normalised.StartsWith(GtinIdentifier, StringComparison.Ordinal))
        {
            return false;
        }

        var digits = normalised[GtinIdentifier.Length..];

        if (digits.Length < GtinElementLength)
        {
            return false;
        }

        var candidate = digits[..GtinElementLength];

        return candidate.All(char.IsAsciiDigit) && Gtin.TryNormalize(candidate, out gtin);
    }

    /// <summary>
    /// A short sentence explaining a scan that did not produce a product, written for someone
    /// standing in a shop rather than for a log file.
    /// </summary>
    public static string Explain(ScannedCode code) => code.Kind switch
    {
        ScanKind.BadCheckDigit =>
            "That did not read as a valid barcode. Try again, holding the packet flat and steady.",

        ScanKind.NotAProduct when LooksLikeUrl(code.Raw) =>
            "That is a web link, not a product barcode. Scan the barcode with the black bars instead.",

        ScanKind.NotAProduct =>
            "That is a code of some kind, but not a product barcode. Look for the one with black bars and digits underneath.",

        ScanKind.Empty => "Nothing was read.",
        _ => string.Empty,
    };

    private static bool LooksLikeUrl(string raw) =>
        raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || raw.StartsWith("upi://", StringComparison.OrdinalIgnoreCase);
}
