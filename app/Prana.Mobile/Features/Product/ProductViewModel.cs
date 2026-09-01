using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Prana.Core.Model;
using Prana.Data;
using Prana.Mobile.Services;

namespace Prana.Mobile.Features.Product;

/// <summary>
/// One product, looked up in the installed catalogue.
/// </summary>
/// <remarks>
/// F10 turns this into the real product screen with nutrition indicators and ingredient
/// attributes. What matters here is that the three outcomes are already distinct, because
/// collapsing them is the mistake that would be expensive to unpick later:
///
/// found and complete, found but holding nothing beyond a name, and not in the catalogue at all.
/// The middle one is 58% of the catalogue and must offer discovery exactly as the third does,
/// per ADR-0026.
/// </remarks>
[QueryProperty(nameof(Barcode), "barcode")]
public partial class ProductViewModel(
    IProductRepository products,
    UserDatabase user,
    CatalogueStartup startup) : ViewModelBase
{
    [ObservableProperty]
    public partial string Barcode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProductName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Explanation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Found { get; set; }

    /// <summary>
    /// True when the catalogue has the product but knows nothing beyond its name, and also when
    /// it does not have it at all. Both cases must offer to look it up online.
    /// </summary>
    [ObservableProperty]
    public partial bool NeedsDiscovery { get; set; }

    partial void OnBarcodeChanged(string value) => _ = LoadAsync(value);

    private async Task LoadAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await startup.EnsureReadyAsync(CancellationToken.None);

            var product = await products.FindAsync(barcode, CancellationToken.None);

            // Recorded whether or not it was found. The misses are the demand signal the
            // catalogue grows by, and they stay on the device until the user submits one.
            user.RecordScan(barcode, product is not null, DateTimeOffset.UtcNow);

            if (product is null)
            {
                Found = false;
                NeedsDiscovery = true;
                ProductName = "Not in the catalogue";
                Summary = string.Empty;

                Explanation = "This barcode is not in the installed catalogue. That does not mean "
                    + "the product does not exist. Searching online for it arrives in a later version.";

                return;
            }

            Found = true;
            ProductName = product.Name;

            var incomplete = product.Nutrition is null && string.IsNullOrEmpty(product.IngredientsRaw);
            NeedsDiscovery = incomplete;

            Summary = Describe(product);

            Explanation = incomplete
                ? "The catalogue has this product but nothing about what is in it. Looking it up "
                    + "online arrives in a later version."
                : $"{Verification(product)} Nutrition detail arrives in a later version.";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Found = false;
            NeedsDiscovery = false;
            ErrorMessage = "The catalogue could not be read.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// A one-line summary of what is known. Deliberately states the basis alongside any number,
    /// because a value without its basis is not a fact about the product.
    /// </summary>
    private static string Describe(ProductRecord product)
    {
        var parts = new List<string>();

        if (product.Brand is { Length: > 0 } brand)
        {
            parts.Add(brand);
        }

        if (product.Package is { } package)
        {
            var unit = package.Quantity.Unit switch
            {
                Unit.Gram => "g",
                Unit.Kilogram => "kg",
                Unit.Millilitre => "ml",
                Unit.Litre => "l",
                _ => string.Empty,
            };

            parts.Add($"{package.Quantity.Value.ToString("0.#", CultureInfo.CurrentCulture)} {unit}".Trim());
        }

        if (product.Nutrition is { Count: > 0 } nutrition)
        {
            var basis = nutrition[0].Basis switch
            {
                NutritionBasis.Per100g => "per 100 g",
                NutritionBasis.Per100ml => "per 100 ml",
                NutritionBasis.PerServing => "per serving",
                _ => "per package",
            };

            parts.Add($"nutrition {basis}");
        }

        if (!string.IsNullOrEmpty(product.IngredientsRaw))
        {
            parts.Add("ingredients listed");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// How much to trust this. Almost the whole catalogue is unverified community data, and
    /// saying so plainly is the point rather than a caveat to hide.
    /// </summary>
    private static string Verification(ProductRecord product) => product.Verification.Status switch
    {
        VerificationStatus.Verified => $"Verified against packaging on {product.Verification.LastVerified}.",
        VerificationStatus.Disputed => "Sources disagree about this product, so treat it with care.",
        _ => "Community data, not independently verified. Trust the packet over this app.",
    };

    [RelayCommand]
    private static Task BackAsync() => Shell.Current.GoToAsync("..");
}
