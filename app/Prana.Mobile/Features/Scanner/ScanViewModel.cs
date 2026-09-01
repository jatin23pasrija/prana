using CommunityToolkit.Mvvm.Input;
using Prana.Core.Barcodes;

namespace Prana.Mobile.Features.Scanner;

/// <summary>Which part of the scan screen is in play.</summary>
public enum ScanState
{
    /// <summary>Permission has not been asked for or decided yet.</summary>
    Starting,

    /// <summary>Camera running.</summary>
    Scanning,

    /// <summary>Camera refused. Manual entry is the way through.</summary>
    NoCamera,
}

/// <summary>
/// The scan screen.
/// </summary>
/// <remarks>
/// Manual entry is always present, not a fallback revealed on failure. A camera can be refused,
/// broken, or defeated by a crumpled packet, and an app that can only scan is an app that
/// sometimes cannot be used at all.
///
/// Deciding what a scanned string means lives in <see cref="BarcodeReader"/>, which is tested
/// without a camera. This class only decides what the screen does about it.
/// </remarks>
public partial class ScanViewModel : ViewModelBase
{
    public ScanViewModel()
    {
        Title = "Scan";
        Barcode = string.Empty;
        ValidationMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    [ObservableProperty]
    public partial ScanState State { get; set; } = ScanState.Starting;

    [ObservableProperty]
    public partial bool IsCameraRunning { get; set; }

    [ObservableProperty]
    public partial bool IsTorchOn { get; set; }

    /// <summary>
    /// What just happened, when it was not a product. An unreadable code, a QR code, a payment
    /// code: all of them need a sentence, because a scanner that does nothing looks broken.
    /// </summary>
    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpCommand))]
    public partial string Barcode { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; }

    private bool CanLookUp => Gtin.TryNormalize(Barcode, out _);

    /// <summary>
    /// Asks for the camera, explaining first.
    /// </summary>
    /// <remarks>
    /// A refusal is not an error state. The screen keeps working through manual entry and offers
    /// a way to change the decision, rather than becoming a dead end with a permission message.
    /// </remarks>
    [RelayCommand]
    private async Task StartAsync()
    {
        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.Camera>();
        }

        if (status == PermissionStatus.Granted)
        {
            State = ScanState.Scanning;
            IsCameraRunning = true;
            StatusMessage = "Point the camera at the barcode on the packet.";
            return;
        }

        State = ScanState.NoCamera;
        IsCameraRunning = false;

        StatusMessage = "Prana cannot open the camera. You can still type the number printed "
            + "under the barcode, or allow camera access in settings.";
    }

    /// <summary>Stops the camera when the screen goes away, so it is not left running.</summary>
    [RelayCommand]
    private void Stop()
    {
        IsCameraRunning = false;
        IsTorchOn = false;
    }

    [RelayCommand]
    private static Task OpenSettingsAsync()
    {
        AppInfo.Current.ShowSettingsUI();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void ToggleTorch() => IsTorchOn = !IsTorchOn;

    /// <summary>
    /// Handles one decoded code.
    /// </summary>
    /// <remarks>
    /// A product goes straight to the product screen with no confirmation step. A wrong result is
    /// one tap from Back, whereas a confirm step is a tax on every correct scan, and most scans
    /// are correct.
    /// </remarks>
    public async Task OnBarcodeDetectedAsync(string raw)
    {
        var code = BarcodeReader.Read(raw);

        if (code.Kind != ScanKind.Product || code.Gtin is null)
        {
            // Not a product code. The decoder keeps running, because the next thing someone does
            // is aim at the right symbol on the same packet.
            StatusMessage = BarcodeReader.Explain(code);
            return;
        }

        // Stopped before navigating, so the camera is not decoding behind the product screen and
        // firing again the moment it returns.
        IsCameraRunning = false;
        IsTorchOn = false;

        await Shell.Current.GoToAsync($"{Routes.Product}?barcode={code.Gtin}");
    }

    partial void OnBarcodeChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ValidationMessage = string.Empty;
            return;
        }

        if (Gtin.TryNormalize(value, out _))
        {
            ValidationMessage = string.Empty;
            return;
        }

        // Two different problems. A short number means keep typing; a complete one that fails its
        // check digit almost always means a mistyped digit.
        ValidationMessage = value.Length < 8
            ? "Keep going, a barcode has at least 8 digits."
            : "That is not a valid barcode. Check for a mistyped digit.";
    }

    [RelayCommand(CanExecute = nameof(CanLookUp))]
    private async Task LookUpAsync()
    {
        if (!Gtin.TryNormalize(Barcode, out var gtin))
        {
            return;
        }

        IsCameraRunning = false;
        await Shell.Current.GoToAsync($"{Routes.Product}?barcode={gtin}");
    }
}
