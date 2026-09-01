using CommunityToolkit.Mvvm.Input;

namespace Prana.Mobile.Features.Home;

public partial class HomeViewModel : ViewModelBase
{
    public HomeViewModel()
    {
        Title = "Prana";

        // Replaced in F08, once there is a catalogue to ask. Worded as a fact rather than a
        // placeholder, so that shipping it by accident would be embarrassing rather than
        // misleading.
        CatalogueStatus = "No catalogue installed yet.";
    }

    [ObservableProperty]
    public partial string CatalogueStatus { get; set; }

    [RelayCommand]
    private static Task ScanAsync() => Shell.Current.GoToAsync(Routes.Scan);

    [RelayCommand]
    private static Task SearchAsync() => Shell.Current.GoToAsync(Routes.Search);

    [RelayCommand]
    private static Task GroceryAsync() => Shell.Current.GoToAsync(Routes.Grocery);
}
