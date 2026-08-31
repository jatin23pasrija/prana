namespace Prana.Mobile.Features.Grocery;

public partial class GroceryViewModel : ViewModelBase
{
    public GroceryViewModel()
    {
        Title = "Grocery";

        // The list never leaves the device. Saying so here rather than only in a privacy policy
        // nobody opens is the point.
        EmptyMessage = "Scan or search for a product to add it. Your list stays on this device.";
    }

    [ObservableProperty]
    public partial string EmptyMessage { get; set; }
}
