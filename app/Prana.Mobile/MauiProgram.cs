using CommunityToolkit.Mvvm.ComponentModel;
using Prana.Data;
using Prana.Mobile.Features.Grocery;
using Prana.Mobile.Features.Home;
using Prana.Mobile.Features.Product;
using Prana.Mobile.Features.Scanner;
using Prana.Mobile.Features.Search;
using Prana.Mobile.Features.Settings;
using Prana.Mobile.Services;
using ZXing.Net.Maui.Controls;

namespace Prana.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseBarcodeReader()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        RegisterData(builder.Services);
        RegisterFeatures(builder.Services);

        return builder.Build();
    }

    /// <summary>
    /// Every page and view model is registered here.
    /// </summary>
    /// <remarks>
    /// Pages are transient and view models are transient with them, so navigating back to a
    /// screen gives a fresh one rather than whatever state the last visit left behind. Anything
    /// that genuinely needs to outlive a page, such as the catalogue in F08, becomes a singleton
    /// service instead of being smuggled through a view model.
    /// </remarks>
    /// <summary>
    /// The data layer. Singletons, because they wrap files rather than state: one catalogue, one
    /// user database, and one startup that must not run twice.
    /// </summary>
    private static void RegisterData(IServiceCollection services)
    {
        services.AddSingleton<ICatalogueStorage, MauiCatalogueStorage>();
        services.AddSingleton<CataloguePaths>();
        services.AddSingleton<CatalogueConnection>();
        services.AddSingleton<UserDatabase>();
        services.AddSingleton<CatalogueInstaller>();
        services.AddSingleton<CatalogueStartup>();

        services.AddSingleton<IProductRepository, ProductRepository>();
        services.AddSingleton<ISearchRepository, SearchRepository>();
        services.AddSingleton<IAnalysisRepository, AnalysisRepository>();

        // The rule book is a singleton because the rule files are read from the app package once
        // and never change while the app is running. The analysis holds the ingredient
        // dictionary for the same reason.
        services.AddSingleton<RuleBook>();
        services.AddSingleton<IRuleProvider>(p => p.GetRequiredService<RuleBook>());
        services.AddSingleton<ProductAnalysis>();
    }

    private static void RegisterFeatures(IServiceCollection services)
    {
        services.AddTransient<HomePage>();
        services.AddTransient<HomeViewModel>();

        services.AddTransient<SearchPage>();
        services.AddTransient<SearchViewModel>();

        services.AddTransient<GroceryPage>();
        services.AddTransient<GroceryViewModel>();

        services.AddTransient<SettingsPage>();
        services.AddTransient<SettingsViewModel>();

        services.AddTransient<ScanPage>();
        services.AddTransient<ScanViewModel>();

        services.AddTransient<ProductPage>();
        services.AddTransient<ProductViewModel>();
    }
}

/// <summary>
/// What every view model inherits.
/// </summary>
/// <remarks>
/// <see cref="ObservableObject"/> from the toolkit supplies change notification through source
/// generators, so a property is one attribute rather than fifteen lines. This class adds only
/// the two things every screen in this app turns out to need: whether it is working, and what
/// went wrong.
/// </remarks>
public abstract partial class ViewModelBase : ObservableObject
{
    protected ViewModelBase() => Title = string.Empty;

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>
    /// Set when something failed in a way the user needs to know about. Left null otherwise.
    /// Screens show this instead of their content rather than beside it, because a half-rendered
    /// screen with an error banner is how people end up trusting a number that was never loaded.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    /// <summary>For binding visibility, since XAML cannot test a string for emptiness.</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
}
