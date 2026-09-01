using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Prana.Data;
using Prana.Mobile.Services;

namespace Prana.Mobile.Features.Home;

public partial class HomeViewModel(CatalogueStartup startup) : ViewModelBase
{
    [ObservableProperty]
    public partial string CatalogueStatus { get; set; } = "Checking the catalogue.";

    /// <summary>
    /// Loads the catalogue state.
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
    private static Task ScanAsync() => Shell.Current.GoToAsync(Routes.Scan);

    [RelayCommand]
    private static Task SearchAsync() => Shell.Current.GoToAsync(Routes.Search);

    [RelayCommand]
    private static Task GroceryAsync() => Shell.Current.GoToAsync(Routes.Grocery);
}
