using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Prana.Data;
using Prana.Mobile.Services;

namespace Prana.Mobile.Features.Home;

/// <summary>One line in the recent scans list.</summary>
/// <param name="Gtin">Canonical key, used to reopen the product.</param>
/// <param name="Label">What to show: the product name, or the barcode when it was not found.</param>
/// <param name="Found">Whether the catalogue had it.</param>
public sealed record RecentScan(string Gtin, string Label, bool Found);

public partial class HomeViewModel(
    CatalogueStartup startup,
    UserDatabase user,
    IProductRepository products) : ViewModelBase
{
    [ObservableProperty]
    public partial string CatalogueStatus { get; set; } = "Checking the catalogue.";

    [ObservableProperty]
    public partial bool HasRecentScans { get; set; }

    public ObservableCollection<RecentScan> RecentScans { get; } = [];

    /// <summary>
    /// Loads the catalogue state and the recent scans.
    /// </summary>
    /// <remarks>
    /// Called when the page appears rather than from the constructor, so the screen is on show
    /// before any file is touched. On a first launch this is unpacking a catalogue, and the plan
    /// is explicit that opening the app must never wait on that.
    /// </remarks>
    [RelayCommand]
    private async Task LoadAsync()
    {
        var status = await startup.EnsureReadyAsync(CancellationToken.None);
        CatalogueStatus = Describe(status);

        await LoadRecentAsync();
    }

    private async Task LoadRecentAsync()
    {
        RecentScans.Clear();

        try
        {
            foreach (var (gtin, found) in user.RecentScans(10))
            {
                // The name is looked up rather than stored with the scan. A catalogue update can
                // rename a product or fill in one that was previously missing, and history that
                // showed the old answer would be quietly wrong.
                var product = found
                    ? await products.FindAsync(gtin, CancellationToken.None)
                    : null;

                RecentScans.Add(new RecentScan(gtin, product?.Name ?? gtin, product is not null));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            // History is a convenience. Losing it must never stop the home screen appearing.
        }

        HasRecentScans = RecentScans.Count > 0;
    }

    /// <summary>
    /// Says what is actually installed, in plain words.
    /// </summary>
    /// <remarks>
    /// Shown on the home screen because someone who does not know whether they have data cannot
    /// tell a missing product from a missing catalogue, and will blame the app for the wrong
    /// thing. It also names the starter catalogue as partial rather than letting it look
    /// complete, since a shopper who scans three unknown products in a row deserves to know why.
    /// </remarks>
    private static string Describe(CatalogueStatus status)
    {
        if (!status.IsInstalled)
        {
            return "No catalogue installed. Product lookup will not work until one is downloaded.";
        }

        var count = status.ProductCount.ToString("N0", CultureInfo.CurrentCulture);

        return status.Kind == "starter"
            ? $"Starter catalogue, {count} products, built {status.BuiltOn}. "
                + "This is a small selection. The full catalogue is much larger."
            : $"{count} products, built {status.BuiltOn}. Works without internet.";
    }

    [RelayCommand]
    private static Task OpenRecentAsync(RecentScan? scan) =>
        scan is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{Routes.Product}?barcode={scan.Gtin}");

    [RelayCommand]
    private static Task ScanAsync() => Shell.Current.GoToAsync(Routes.Scan);

    [RelayCommand]
    private static Task SearchAsync() => Shell.Current.GoToAsync(Routes.Search);

    [RelayCommand]
    private static Task GroceryAsync() => Shell.Current.GoToAsync(Routes.Grocery);
}
