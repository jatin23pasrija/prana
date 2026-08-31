namespace Prana.Mobile.Features.Search;

public partial class SearchPage : ContentPage
{
    public SearchPage(SearchViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
