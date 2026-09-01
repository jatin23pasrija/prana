using CommunityToolkit.Mvvm.Input;
using Prana.Core.Barcodes;

namespace Prana.Mobile.Features.Scanner;

/// <summary>
/// Barcode entry. The camera arrives in F09; manual entry works now.
/// </summary>
/// <remarks>
/// Validation is not a placeholder. It calls the same <see cref="Gtin"/> rules the validator and
/// the importer use, so a barcode the app accepts is a barcode the catalogue could contain, and
/// a mistyped digit is caught before it becomes a fruitless lookup or a bad contribution.
/// </remarks>
public partial class ScanViewModel : ViewModelBase
{
    public ScanViewModel()
    {
        Title = "Scan";
        Barcode = string.Empty;
        ValidationMessage = string.Empty;
        Explanation = "Type the number printed under the barcode instead. "
            + "Manual entry stays available even once the camera works.";
    }

    [ObservableProperty]
    public partial string Explanation { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LookUpCommand))]
    public partial string Barcode { get; set; }

    [ObservableProperty]
    public partial string ValidationMessage { get; set; }

    /// <summary>
    /// Whether what has been typed is a real barcode. Recomputed as the field changes, so the
    /// button is disabled rather than the user pressing it and being told no.
    /// </summary>
    private bool CanLookUp => Gtin.TryNormalize(Barcode, out _);

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

        // Two different problems, because "invalid barcode" tells someone nothing about what to
        // do next. A short number means keep typing; a complete one that fails its check digit
        // almost always means a mistyped digit.
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

        await Shell.Current.GoToAsync($"{Routes.Product}?barcode={gtin}");
    }
}
