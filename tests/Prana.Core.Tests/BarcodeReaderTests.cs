using Prana.Core.Barcodes;
using Xunit;

namespace Prana.Core.Tests;

/// <summary>
/// The scanner accepts a wide set of symbologies, so it will be pointed at things that are not
/// product barcodes. These tests cover what it does with them, because failing silently in front
/// of someone holding a packet is the worst available outcome.
/// </summary>
public sealed class BarcodeReaderTests
{
    [Theory]
    [InlineData("8901719134845", "08901719134845")] // EAN-13, the common Indian case
    [InlineData("036000291452", "00036000291452")] // UPC-A
    [InlineData("96385074", "00000096385074")] // EAN-8
    [InlineData("  8901719134845  ", "08901719134845")] // decoders sometimes pad
    public void A_plain_barcode_becomes_a_product_key(string raw, string expected)
    {
        var code = BarcodeReader.Read(raw);

        Assert.Equal(ScanKind.Product, code.Kind);
        Assert.Equal(expected, code.Gtin);
    }

    [Theory]
    // Human-readable form, as printed beneath a GS1 symbol.
    [InlineData("(01)08901719134845", "08901719134845")]
    // What a decoder actually returns: no brackets.
    [InlineData("0108901719134845", "08901719134845")]
    // With a batch number after the GTIN. Everything past the GTIN is ignored on purpose.
    [InlineData("010890171913484510ABC123", "08901719134845")]
    // Group separator, which decoders commonly leave in.
    [InlineData("010890171913484510ABC123", "08901719134845")]
    // Symbology identifier prefix, which some decoders prepend.
    [InlineData("]d20108901719134845", "08901719134845")]
    public void A_gs1_element_string_yields_the_gtin_inside_it(string raw, string expected)
    {
        // This is the whole payoff of enabling DataMatrix and GS1-128. Without it these decode
        // perfectly and then look to the user like a failed scan.
        var code = BarcodeReader.Read(raw);

        Assert.Equal(ScanKind.Product, code.Kind);
        Assert.Equal(expected, code.Gtin);
    }

    [Fact]
    public void Digits_that_fail_their_check_digit_are_told_apart_from_rubbish()
    {
        // Worth separating: this means aim again, not this is not a product.
        var code = BarcodeReader.Read("8901719134846");

        Assert.Equal(ScanKind.BadCheckDigit, code.Kind);
        Assert.Null(code.Gtin);
        Assert.Contains("Try again", BarcodeReader.Explain(code));
    }

    [Theory]
    [InlineData("https://example.invalid/product/123")]
    [InlineData("upi://pay?pa=someone@bank")]
    public void A_link_is_named_as_a_link(string raw)
    {
        var code = BarcodeReader.Read(raw);

        Assert.Equal(ScanKind.NotAProduct, code.Kind);
        Assert.Contains("web link", BarcodeReader.Explain(code));
    }

    [Theory]
    [InlineData("SOME-SERIAL-1234")]
    [InlineData("BATCH:2026-09")]
    public void Any_other_code_says_what_to_look_for_instead(string raw)
    {
        var code = BarcodeReader.Read(raw);

        Assert.Equal(ScanKind.NotAProduct, code.Kind);
        Assert.Contains("black bars", BarcodeReader.Explain(code));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_is_not_an_error(string? raw) =>
        Assert.Equal(ScanKind.Empty, BarcodeReader.Read(raw).Kind);

    [Fact]
    public void A_gs1_string_with_a_truncated_gtin_is_not_guessed_at()
    {
        // Better to report a failed scan than to pad a short number into a plausible-looking key
        // and send someone to the wrong product.
        var code = BarcodeReader.Read("(01)0890171913");

        Assert.NotEqual(ScanKind.Product, code.Kind);
        Assert.Null(code.Gtin);
    }

    [Fact]
    public void The_raw_text_is_always_kept()
    {
        // Needed to explain the failure, and to report a decoder problem later.
        const string raw = "SOME-SERIAL-1234";

        Assert.Equal(raw, BarcodeReader.Read(raw).Raw);
    }
}
