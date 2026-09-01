namespace Prana.Mobile.Features.Grocery;

public partial class GroceryPage : ContentPage
{
    public GroceryPage(GroceryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
