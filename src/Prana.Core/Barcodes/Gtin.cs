using System.Diagnostics.CodeAnalysis;

namespace Prana.Core.Barcodes;

/// <summary>
/// The canonical product key and the rules that produce it.
/// </summary>
/// <remarks>
/// EAN-13, UPC-A, EAN-8 and GTIN-14 are the same numbering scheme at different widths. A UPC-A
/// code is the EAN-13 of the same product with the leading zero dropped. If records were keyed
/// on the digits as printed, one product would be stored twice under two keys, and no amount of
/// later deduplication would reliably put it back together. So every barcode is zero-padded to
/// 14 digits before it becomes a key, and the printed form is kept separately for display.
///
/// This lives in Prana.Core rather than in the validator because the scanner, the importer and
/// the catalogue builder all need exactly the same answer. Three implementations of this would
/// eventually be three different implementations.
/// </remarks>
public static class Gtin
{
    /// <summary>Width of the canonical key.</summary>
    public const int CanonicalLength = 14;

    /// <summary>
    /// Turns a barcode as printed into the canonical GTIN-14 key, rejecting anything that is
    /// not a plausible barcode. The check digit is verified, because a mistyped barcode that
    /// silently becomes a key is a duplicate record waiting to happen.
    /// </summary>
    public static bool TryNormalize(string? printed, [NotNullWhen(true)] out string? gtin14)
    {
        gtin14 = null;

        if (string.IsNullOrWhiteSpace(printed))
        {
            return false;
        }

        var digits = printed.Trim();

        if (!IsAllDigits(digits) || digits.Length is < 8 or > CanonicalLength)
        {
            return false;
        }

        if (!HasValidCheckDigit(digits))
        {
            return false;
        }

        gtin14 = digits.PadLeft(CanonicalLength, '0');
        return true;
    }

    /// <summary>
    /// Whether the last digit of <paramref name="digits"/> is the correct GS1 check digit for
    /// the digits before it. Leading zeros do not affect the result, so this gives the same
    /// answer for a code before and after padding.
    /// </summary>
    public static bool HasValidCheckDigit(string? digits)
    {
        if (string.IsNullOrEmpty(digits) || digits.Length < 8 || !IsAllDigits(digits))
        {
            return false;
        }

        var body = digits[..^1];
        var declared = digits[^1] - '0';

        return ComputeCheckDigit(body) == declared;
    }

    /// <summary>
    /// The GS1 check digit for a body of digits, meaning the code without its final digit.
    /// Weights alternate 3 and 1 from the right, which is why this is calculated in reverse
    /// rather than by position from the left: it then works for every code width unchanged.
    /// </summary>
    public static int ComputeCheckDigit(string body)
    {
        ArgumentException.ThrowIfNullOrEmpty(body);

        if (!IsAllDigits(body))
        {
            throw new ArgumentException("A barcode body must contain digits only.", nameof(body));
        }

        var total = 0;

        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            total += i % 2 == 0 ? digit * 3 : digit;
        }

        return (10 - (total % 10)) % 10;
    }

    /// <summary>
    /// The directory a product record is stored in, taken from the first three digits of the
    /// canonical key. Sharding keeps any single directory small enough that Git, file explorers
    /// and code review all stay usable at tens of thousands of records.
    /// </summary>
    public static string ShardFor(string gtin14)
    {
        ArgumentException.ThrowIfNullOrEmpty(gtin14);

        if (gtin14.Length != CanonicalLength || !IsAllDigits(gtin14))
        {
            throw new ArgumentException(
                $"Expected a {CanonicalLength} digit canonical GTIN, got '{gtin14}'.",
                nameof(gtin14));
        }

        // Padding pushes the meaningful prefix to the right, so the shard is taken from the
        // first three significant digits rather than from a run of zeros that every record
        // would share.
        var significant = gtin14.TrimStart('0');

        return significant.Length >= 3
            ? significant[..3]
            : significant.PadLeft(3, '0');
    }

    /// <summary>The path a product record belongs at, relative to the data directory.</summary>
    public static string RelativePathFor(string gtin14) =>
        $"products/{ShardFor(gtin14)}/{gtin14}.json";

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
