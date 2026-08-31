using Prana.Mobile.Features.Product;
using Prana.Mobile.Features.Scanner;

namespace Prana.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pages pushed on top of a tab rather than living in the tab bar. Registering them by
        // route means navigation is a string, which is what the contribution flow in F13 needs
        // when it comes back from a browser.
        Routing.RegisterRoute(Routes.Scan, typeof(ScanPage));
        Routing.RegisterRoute(Routes.Product, typeof(ProductPage));
    }
}

/// <summary>
/// Route names, in one place so a typo is a compile error rather than a page that silently
/// fails to open.
/// </summary>
public static class Routes
{
    public const string Home = "//home";
    public const string Search = "//search";
    public const string Grocery = "//grocery";
    public const string Settings = "//settings";

    public const string Scan = "scan";
    public const string Product = "product";
}
