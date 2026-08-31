namespace Prana.Mobile.Features.Search;

public partial class SearchViewModel : ViewModelBase
{
    public SearchViewModel()
    {
        Title = "Search";
        Query = string.Empty;
        EmptyMessage = "Catalogue search arrives with the offline catalogue.";
    }

    [ObservableProperty]
    public partial string Query { get; set; }

    [ObservableProperty]
    public partial string EmptyMessage { get; set; }
}
