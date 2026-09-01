using ZXing.Net.Maui;

namespace Prana.Mobile.Features.Scanner;

/// <summary>
/// Wraps the camera and the decoder so nothing else in the app knows which decoder is in use.
/// </summary>
/// <remarks>
/// This is the abstraction ADR-0006 calls for. The project ships a fully open-source decoder,
/// and if the real-world hit rate on worn Indian packaging turns out to be poor, a Google ML Kit
/// build flavour replaces this one file. Nothing above it changes, because everything above it
/// only ever receives a string.
/// </remarks>
public partial class BarcodeScannerView : ContentView
{
    /// <summary>
    /// Raised for each decoded code, on the main thread. The payload is the raw decoder text,
    /// deliberately not a product key: deciding what a code means belongs to
    /// <see cref="Prana.Core.Barcodes.BarcodeReader"/>, which is tested without a camera.
    /// </summary>
    public event EventHandler<string>? BarcodeDetected;

    public BarcodeScannerView()
    {
        InitializeComponent();

        Reader.Options = new BarcodeReaderOptions
        {
            // Every format the decoder supports, as asked for. The cost is real: more
            // symbologies means slower decoding and more misreads, so BarcodeReader treats
            // anything that is not a product code as a first-class outcome with its own
            // explanation rather than a silent failure.
            Formats = BarcodeFormats.All,

            // GS1 DataMatrix and GS1-128 appear on more and more Indian packaging, and the GTIN
            // sits inside an element string rather than being the whole payload. This tells the
            // decoder to expect that shape.
            AssumeGS1 = true,

            // One code at a time. Multiple results from one frame would mean choosing between
            // them, and choosing wrong sends someone to the wrong product.
            Multiple = false,

            // Slower but markedly better on creased, curved and poorly printed labels, which is
            // most of what a shop actually contains.
            TryHarder = true,
            TryInverted = true,
            AutoRotate = true,

            // The debounce. The decoder itself suppresses repeats of the same code, which is
            // steadier than doing it above and lets the camera keep running for the next packet.
            DelayBetweenContinuousScans = 2000,
            DelayBetweenAnalyzingFrames = 100,
        };
    }

    /// <summary>Whether the camera is actively decoding. Turned off while a result is handled.</summary>
    public bool IsDetecting
    {
        get => Reader.IsDetecting;
        set => Reader.IsDetecting = value;
    }

    /// <summary>The torch. Shop lighting is often the reason a scan fails.</summary>
    public bool IsTorchOn
    {
        get => Reader.IsTorchOn;
        set => Reader.IsTorchOn = value;
    }

    private void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results?.FirstOrDefault()?.Value;

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        // The decoder raises this from a camera thread. Everything downstream touches the UI, so
        // it has to be marshalled before anyone else sees it.
        Dispatcher.Dispatch(() => BarcodeDetected?.Invoke(this, value));
    }
}
