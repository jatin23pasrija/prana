namespace Prana.Mobile.Features.Product;

/// <summary>
/// The product screen. Today it only proves that a barcode survives navigation; F10 fills it in.
/// </summary>
[QueryProperty(nameof(Barcode), "barcode")]
public partial class ProductViewModel : ViewModelBase
{
    public ProductViewModel()
    {
        Title = "Product";
        Barcode = string.Empty;
        Explanation = "Product details arrive with the offline catalogue.";
    }

    [ObservableProperty]
    public partial string Barcode { get; set; }

    [ObservableProperty]
    public partial string Explanation { get; set; }
}
