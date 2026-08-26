using System.Text.Json;
using Prana.Core.Barcodes;
using Xunit;

namespace Prana.Core.Tests;

/// <summary>
/// The canonical key decides whether two scans of the same packet reach the same record.
/// Getting it wrong does not throw, it quietly creates duplicates, so it is tested directly.
/// </summary>
public sealed class GtinTests
{
    [Theory]
    [InlineData("8901234567890", "08901234567890")] // EAN-13, the common Indian case
    [InlineData("08901234567890", "08901234567890")] // already canonical
    [InlineData("036000291452", "00036000291452")] // UPC-A
    [InlineData("96385074", "00000096385074")] // EAN-8
    public void Printed_barcodes_normalise_to_a_canonical_key(string printed, string expected)
    {
        Assert.True(Gtin.TryNormalize(printed, out var gtin));
        Assert.Equal(expected, gtin);
    }

    [Fact]
    public void The_same_product_as_UPC_A_and_EAN_13_produces_one_key()
    {
        // This is the whole reason the canonical key exists. A UPC-A code and the EAN-13 of the
        // same product differ only by a leading zero, and keying on the printed digits would
        // store one product as two records.
        Assert.True(Gtin.TryNormalize("036000291452", out var fromUpc));
        Assert.True(Gtin.TryNormalize("0036000291452", out var fromEan));

        Assert.Equal(fromUpc, fromEan);
    }

    [Theory]
    [InlineData("8901234567891")] // check digit is wrong by one
    [InlineData("890123456789")] // a digit was dropped
    [InlineData("89012345678901234")] // too long
    [InlineData("890123456789O")] // letter O typed for zero
    [InlineData("")]
    [InlineData(null)]
    public void Implausible_barcodes_are_rejected(string? printed)
    {
        Assert.False(Gtin.TryNormalize(printed, out var gtin));
        Assert.Null(gtin);
    }

    [Theory]
    [InlineData("890123456789", 0)]
    [InlineData("890111111111", 6)]
    [InlineData("890222222222", 7)]
    public void Check_digits_are_computed_the_GS1_way(string body, int expected) =>
        Assert.Equal(expected, Gtin.ComputeCheckDigit(body));

    [Fact]
    public void Padding_does_not_change_whether_a_check_digit_is_valid()
    {
        Assert.True(Gtin.HasValidCheckDigit("8901234567890"));
        Assert.True(Gtin.HasValidCheckDigit("08901234567890"));
    }

    [Theory]
    [InlineData("08901234567890", "890")]
    [InlineData("00036000291452", "360")]
    public void Shards_come_from_the_first_significant_digits(string gtin, string expected) =>
        Assert.Equal(expected, Gtin.ShardFor(gtin));

    [Fact]
    public void A_record_path_is_derived_from_the_key_alone() =>
        Assert.Equal(
            "products/890/08901234567890.json",
            Gtin.RelativePathFor("08901234567890"));

    [Fact]
    public void Every_example_record_carries_a_real_barcode()
    {
        // The examples are the fixtures the F03 validator will be built against. A wrong check
        // digit in a fixture would make the validator look broken when it is working.
        foreach (var file in Directory.EnumerateFiles(RepositoryPaths.ValidExamples, "product-*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            var printed = root.GetProperty("barcode_printed").GetString()!;
            var gtin = root.GetProperty("gtin").GetString()!;

            Assert.True(
                Gtin.TryNormalize(printed, out var normalised),
                $"{Path.GetFileName(file)} has an implausible printed barcode: {printed}");

            Assert.Equal(gtin, normalised);
        }
    }
}
