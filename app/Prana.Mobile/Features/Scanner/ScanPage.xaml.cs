namespace Prana.Mobile.Features.Scanner;

public partial class ScanPage : ContentPage
{
    private readonly ScanViewModel _viewModel;

    public ScanPage(ScanViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        Scanner.BarcodeDetected += OnBarcodeDetected;
    }

    /// <summary>
    /// The camera starts when the page appears and stops when it leaves.
    /// </summary>
    /// <remarks>
    /// Leaving it running behind another page keeps the sensor and the torch on, drains the
    /// battery, and leaves an app holding a live camera it is not using, which is not a
    /// reasonable thing to do on someone's phone.
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.StartCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopCommand.Execute(null);
    }

    private async void OnBarcodeDetected(object? sender, string raw) =>
        await _viewModel.OnBarcodeDetectedAsync(raw);
}
