using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Prana.Data;
using Prana.Mobile.Services;

namespace Prana.Mobile.Features.Search;

public partial class SearchViewModel(ISearchRepository search, CatalogueStartup startup) : ViewModelBase
{
    /// <summary>
    /// How long to wait after the last keystroke before searching. Long enough that typing a word
    /// runs one query rather than six, short enough to feel immediate.
    /// </summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _pending;

    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EmptyMessage { get; set; } = "Type to search the installed catalogue.";

    [ObservableProperty]
    public partial bool HasResults { get; set; }

    public ObservableCollection<ProductSummary> Results { get; } = [];

    partial void OnQueryChanged(string value) => _ = SearchAsync(value);

    private async Task SearchAsync(string query)
    {
        // Every keystroke cancels the search the previous one started. Without this, a slow query
        // for "par" can land after the one for "parle" and overwrite better results with worse.
        var previous = _pending;
        _pending = new CancellationTokenSource();
        var token = _pending.Token;

        if (previous is not null)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        try
        {
            await Task.Delay(Debounce, token);

            var status = await startup.EnsureReadyAsync(token);

            if (!status.IsInstalled)
            {
                Results.Clear();
                HasResults = false;
                EmptyMessage = "No catalogue installed yet, so there is nothing to search.";
                return;
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                Results.Clear();
                HasResults = false;
                EmptyMessage = "Type to search the installed catalogue.";
                return;
            }

            IsBusy = true;

            var found = await search.SearchAsync(query, limit: 50, token);

            token.ThrowIfCancellationRequested();

            Results.Clear();

            foreach (var product in found)
            {
                Results.Add(product);
            }

            HasResults = Results.Count > 0;

            EmptyMessage = HasResults
                ? string.Empty
                : $"Nothing in the catalogue matches \"{query}\". "
                    + "It may still exist; the catalogue does not have every product yet.";
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke. Leaving the previous results on screen is right:
            // clearing them would make the list flicker on every character.
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static Task OpenAsync(ProductSummary? product) =>
        product is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"{Routes.Product}?barcode={product.Gtin}");
}
