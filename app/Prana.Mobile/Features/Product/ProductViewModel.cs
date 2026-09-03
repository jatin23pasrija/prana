using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Prana.Core.Model;
using Prana.Core.Rules;
using Prana.Data;
using Prana.Mobile.Services;

namespace Prana.Mobile.Features.Product;

/// <summary>One nutrient row, ready for the table.</summary>
public sealed record NutrientLine(string Name, string Value, bool IsKnown);

/// <summary>
/// One indicator as the screen shows it, with everything needed to explain itself on tap.
/// </summary>
public sealed record IndicatorLine(
    string Name,
    string Level,
    string Value,
    string Statement,
    string RuleName,
    string SourceLine,
    string? CalculatedNote,
    string? DerivedNote)
{
    /// <summary>The full text of the explanation sheet.</summary>
    public string Explanation
    {
        get
        {
            var parts = new List<string> { Statement, string.Empty, RuleName, SourceLine };

            if (DerivedNote is not null)
            {
                parts.Add(string.Empty);
                parts.Add(DerivedNote);
            }

            if (CalculatedNote is not null)
            {
                parts.Add(string.Empty);
                parts.Add(CalculatedNote);
            }

            return string.Join(Environment.NewLine, parts);
        }
    }
}

/// <summary>One nutrition panel as the screen shows it.</summary>
public sealed record PanelLine(
    string Title,
    string? Serving,
    IReadOnlyList<NutrientLine> Nutrients,
    IReadOnlyList<IndicatorLine> Indicators,
    string? NotDeclaredNote)
{
    public bool HasServing => !string.IsNullOrEmpty(Serving);

    public bool HasIndicators => Indicators.Count > 0;

    public bool HasNotDeclared => !string.IsNullOrEmpty(NotDeclaredNote);
}

/// <summary>
/// One product, looked up in the installed catalogue and analysed.
/// </summary>
/// <remarks>
/// Three outcomes are kept apart, and collapsing any two of them would be the expensive mistake:
/// found and complete, found but holding nothing beyond a name, and not in the catalogue at all.
/// The middle case is 58 per cent of the catalogue and must offer discovery exactly as the third
/// does, which is ADR-0026 and the reason the record is not simply treated as a hit.
/// </remarks>
[QueryProperty(nameof(Barcode), "barcode")]
public partial class ProductViewModel(
    IProductRepository products,
    ProductAnalysis analysis,
    UserDatabase user,
    CatalogueStartup startup) : ViewModelBase
{
    [ObservableProperty]
    public partial string Barcode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProductName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSummary { get; set; }

    [ObservableProperty]
    public partial string Explanation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Found { get; set; }

    /// <summary>
    /// True when the catalogue has the product but knows nothing beyond its name, and also when
    /// it does not have it at all. Both must offer to look it up online.
    /// </summary>
    [ObservableProperty]
    public partial bool NeedsDiscovery { get; set; }

    [ObservableProperty]
    public partial bool HasNutrition { get; set; }

    [ObservableProperty]
    public partial bool HasIngredients { get; set; }

    [ObservableProperty]
    public partial string IngredientsRaw { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PalmHeading { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PalmStatement { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPalmDetail { get; set; }

    [ObservableProperty]
    public partial string FreshnessNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string VerificationNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStale { get; set; }

    [ObservableProperty]
    public partial string SourcesNote { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPeerComparisons { get; set; }

    public ObservableCollection<PanelLine> Panels { get; } = [];

    public ObservableCollection<string> PeerComparisons { get; } = [];

    partial void OnBarcodeChanged(string value) => _ = LoadAsync(value);

    private async Task LoadAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        IsBusy = true;

        try
        {
            await startup.EnsureReadyAsync(CancellationToken.None);

            var product = await products.FindAsync(barcode, CancellationToken.None);

            // Recorded whether or not it was found. The misses are the demand signal the
            // catalogue grows by, and they stay on the device until the user submits one.
            user.RecordScan(barcode, product is not null, DateTimeOffset.UtcNow);

            if (product is null)
            {
                ShowMiss();
                return;
            }

            var result = await analysis.AnalyseAsync(
                product,
                DateOnly.FromDateTime(DateTime.Now),
                CancellationToken.None);

            Show(result);
        }
        // Deliberately catches everything. This runs as a fire-and-forget task from a property
        // setter, so an exception that escapes here is not merely unhandled, it is invisible:
        // the task faults, nothing observes it, and the screen renders half-populated with no
        // error anywhere. That is exactly what happened with a catalogue built before the peer
        // statistics table existed, and the symptom was a product page showing a barcode and
        // nothing else, with a clean logcat.
        catch (Exception ex)
        {
            Found = false;
            NeedsDiscovery = false;

            ErrorMessage = ex is IOException or InvalidDataException
                ? "The catalogue could not be read."
                : "Something went wrong reading this product. "
                  + $"The catalogue may be from an incompatible version. ({ex.GetType().Name})";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowMiss()
    {
        Found = false;
        NeedsDiscovery = true;
        HasNutrition = false;
        HasIngredients = false;
        ProductName = "Not in the catalogue";
        Summary = string.Empty;
        HasSummary = false;

        Explanation = "This barcode is not in the installed catalogue. That does not mean the "
            + "product does not exist, only that nobody has added it yet.";
    }

    private void Show(ProductAnalysisResult result)
    {
        var product = result.Product;

        Found = true;
        ProductName = product.Name;
        Summary = Describe(product, result.BrandName);
        HasSummary = Summary.Length > 0;

        // ADR-0026. A record holding only a name makes the lookup succeed, and without this the
        // app would stop offering discovery for exactly the products that most need it.
        NeedsDiscovery = !result.IsComplete;

        Explanation = result.IsComplete
            ? string.Empty
            : "The catalogue has this product but nothing about what is in it: no nutrition "
              + "panel and no ingredients. That is a gap in the data, not a fact about the food.";

        Panels.Clear();

        foreach (var panel in result.Panels)
        {
            Panels.Add(ToLine(panel));
        }

        HasNutrition = Panels.Count > 0;

        IngredientsRaw = product.IngredientsRaw ?? string.Empty;
        HasIngredients = IngredientsRaw.Length > 0;

        (PalmHeading, PalmStatement) = Palm(result.Palm);
        HasPalmDetail = result.Palm.State != PalmState.NoIngredients;

        PeerComparisons.Clear();

        foreach (var sentence in result.PeerComparisons)
        {
            PeerComparisons.Add(sentence);
        }

        HasPeerComparisons = PeerComparisons.Count > 0;

        FreshnessNote = result.FreshnessNote;
        IsStale = result.Freshness is Freshness.ReviewRecommended or Freshness.PossiblyOutdated;
        VerificationNote = Verification(product);
        SourcesNote = SourceList(product);
    }

    private static PanelLine ToLine(PanelView panel)
    {
        var nutrients = panel.Rows
            .Select(r => new NutrientLine(r.Display, r.DisplayValue, r.IsKnown))
            .ToList();

        var indicators = panel.Indicators.Select(i => new IndicatorLine(
            i.Display,
            Level(i.Level),
            IndicatorValue(i),
            i.Statement,
            $"Rule: {i.RuleTitle} v{i.RuleVersion} ({i.RuleId})",
            $"Source: {i.Source.Title}, {i.Source.Publisher}. {i.Source.Locator}. "
                + $"Published under {i.Source.Licence}.",
            // The statement already says the figure was calculated. This adds why, without
            // repeating it: the two appeared one after the other in the same sheet.
            i.CalculatedFrom is null
                ? null
                : "The packet prints only a per-serving panel, so this is our arithmetic rather "
                  + "than a figure from the label. ADR-0033 permits it for display and forbids "
                  + "writing it back to the record.",
            i.DerivedNote is null ? null : "How this was worked out: " + i.DerivedNote))
            .ToList();

        // Naming what the packet states is absent, so it reads as checked rather than missed.
        var notDeclared = panel.NotDeclared.Count == 0
            ? null
            : "The packet does not declare: "
              + string.Join(", ", panel.NotDeclared.Select(Nutrients.DisplayName).Select(n => n.ToLowerInvariant()))
              + ".";

        return new PanelLine(panel.BasisLabel, panel.ServingLabel, nutrients, indicators, notDeclared);
    }

    private static string IndicatorValue(Indicator indicator) =>
        indicator.Value.ToString("0.##", CultureInfo.InvariantCulture) + " " + indicator.Unit;

    private static string Level(IndicatorLevel level) => level switch
    {
        IndicatorLevel.Lower => "Lower",
        IndicatorLevel.Higher => "Higher",
        IndicatorLevel.AboveLimit => "Above limit",
        IndicatorLevel.WithinLimit => "Within limit",
        _ => "Moderate",
    };

    /// <summary>
    /// The four palm states. The wording is the feature: "not detected in the ingredients we
    /// hold" and "does not contain palm" are different claims, and only the first is true.
    /// </summary>
    private static (string Heading, string Statement) Palm(PalmFinding finding) => finding.State switch
    {
        PalmState.ConfirmedQuantity => ("Palm: present, quantity declared", finding.Statement),
        PalmState.Present => ("Palm: present", finding.Statement),
        PalmState.Unknown => ("Palm: not stated", finding.Statement),
        PalmState.NotDetected => ("Palm: not detected", finding.Statement),
        _ => ("Palm: unknown", finding.Statement),
    };

    /// <summary>
    /// A one-line summary of what is known. Deliberately states the basis alongside any number,
    /// because a value without its basis is not a fact about the product.
    /// </summary>
    private static string Describe(ProductRecord product, string? brandName)
    {
        var parts = new List<string>();

        // The brand's display name, not the slug the record stores. Showing the slug puts "amul"
        // on screen where the catalogue holds "Amul".
        if (brandName is { Length: > 0 } brand)
        {
            parts.Add(brand);
        }

        if (product.Package is { } package)
        {
            var unit = package.Quantity.Unit switch
            {
                Unit.Gram => "g",
                Unit.Kilogram => "kg",
                Unit.Millilitre => "ml",
                Unit.Litre => "l",
                _ => string.Empty,
            };

            parts.Add($"{package.Quantity.Value.ToString("0.#", CultureInfo.CurrentCulture)} {unit}".Trim());
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// How much to trust this. Almost the whole catalogue is unverified community data, and
    /// saying so plainly is the point rather than a caveat to hide.
    /// </summary>
    private static string Verification(ProductRecord product) => product.Verification.Status switch
    {
        VerificationStatus.Verified => "Verified against packaging by a contributor.",
        VerificationStatus.Disputed => "Sources disagree about this product, so treat it with care.",
        _ => "Community data, not independently verified. Trust the packet over this app.",
    };

    private static string SourceList(ProductRecord product) =>
        product.Sources.Count == 0
            ? "No source recorded."
            : string.Join(
                Environment.NewLine,
                product.Sources.Select(s =>
                    $"• {Describe(s.Type)}, retrieved {s.RetrievedAt}"
                    + (s.Licence is { Length: > 0 } licence ? $", {licence}" : string.Empty)));

    private static string Describe(SourceType type) => type switch
    {
        SourceType.Packaging => "Photograph of the packet",
        SourceType.Manufacturer => "Manufacturer",
        SourceType.Regulator => "Regulator",
        SourceType.OpenDatabase => "Open database",
        SourceType.Retailer => "Retailer",
        _ => "Community contribution",
    };

    [RelayCommand]
    private static async Task ExplainAsync(IndicatorLine? indicator)
    {
        // Every indicator explains itself, naming the rule and its version. Required by the
        // definition of done, and the reason the indicator carries its citation rather than
        // looking one up here.
        if (indicator is not null && Application.Current?.Windows.Count > 0)
        {
            await Shell.Current.DisplayAlertAsync(
                $"{indicator.Name}: {indicator.Level}",
                indicator.Explanation,
                "Close");
        }
    }

    [RelayCommand]
    private static Task BackAsync() => Shell.Current.GoToAsync("..");
}
