using CommunityToolkit.Mvvm.Input;

namespace Prana.Mobile.Features.Settings;

/// <summary>
/// About, attribution, and the things the app promises not to do.
/// </summary>
/// <remarks>
/// Three of the strings on this screen are obligations rather than copy.
///
/// The attribution is required by ODbL, which is share-alike, and DATA_SOURCES.md commits the
/// project to showing it in the app rather than only in a file on GitHub.
///
/// The disclaimer is required by DATA_POLICY.md. The catalogue is community data that will
/// contain mistakes, and an app that presents it without saying so is making a claim the data
/// cannot support.
///
/// The privacy summary is only true as long as it stays true. If anything ever starts leaving
/// the device, this text changes in the same pull request.
/// </remarks>
public partial class SettingsViewModel : ViewModelBase
{
    private const string ProjectUrl = "https://github.com/jatin23pasrija/prana";

    public SettingsViewModel()
    {
        Title = "Settings";

        AppVersion = $"Prana {AppInfo.Current.VersionString} (build {AppInfo.Current.BuildString})";

        // Replaced in F08 with the installed catalogue version.
        CatalogueVersion = "No catalogue installed yet.";

        DataAttribution = "Contains data from Open Food Facts, a community database of food "
            + "products from around the world.";

        DataLicence = "Open Database License (ODbL) v1.0 for the database, Database Contents "
            + "License (DbCL) v1.0 for the contents. Prana publishes its own catalogue under the "
            + "same terms.";

        Disclaimer = "Prana shows what labels declare and compares products with each other. It "
            + "does not tell you whether a food is healthy, and it is not medical or nutritional "
            + "advice. The data is community maintained and will sometimes be wrong or out of "
            + "date, so trust the packet in your hand over this app.";

        PrivacySummary = "There is no account and no tracking. Your grocery list, your scan "
            + "history and your settings stay on this device. Nothing is uploaded unless you "
            + "choose to submit a product, and you are shown what is sent before it goes.";
    }

    [ObservableProperty]
    public partial string AppVersion { get; set; }

    [ObservableProperty]
    public partial string CatalogueVersion { get; set; }

    [ObservableProperty]
    public partial string DataAttribution { get; set; }

    [ObservableProperty]
    public partial string DataLicence { get; set; }

    [ObservableProperty]
    public partial string Disclaimer { get; set; }

    [ObservableProperty]
    public partial string PrivacySummary { get; set; }

    [RelayCommand]
    private static Task OpenProjectAsync() =>
        Browser.Default.OpenAsync(ProjectUrl, BrowserLaunchMode.SystemPreferred);
}
