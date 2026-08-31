namespace Prana.Mobile.Features.Product;

public partial class ProductPage : ContentPage
{
    public ProductPage(ProductViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
