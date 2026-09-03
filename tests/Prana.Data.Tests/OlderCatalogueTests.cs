using Microsoft.Data.Sqlite;
using Prana.Core.Model;
using Prana.Data;
using Xunit;

namespace Prana.Data.Tests;

/// <summary>
/// The app must read a catalogue built before a feature existed.
/// </summary>
/// <remarks>
/// Written after this failed on a device. The app bundled a starter catalogue built two days
/// earlier, which had neither the ingredient dictionary nor the peer statistics table. Every
/// query threw, the exception was swallowed by a fire-and-forget task, and the product screen
/// rendered a barcode and nothing else with a completely clean logcat.
///
/// It matters well beyond that bug. Catalogues are data files with their own release cycle: F11
/// downloads and installs ones the app did not build, and the three APK flavours in ADR-0030
/// bundle different ones. Meeting an older catalogue is normal operation, and the response has to
/// be offering less rather than failing.
/// </remarks>
public sealed class OlderCatalogueTests : IDisposable
{
    private readonly string _directory;
    private readonly string _path;

    public OlderCatalogueTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "prana-old-catalogue", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "catalogue.db");

        // A catalogue with products but none of the tables F10 added.
        using var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE product (gtin TEXT PRIMARY KEY, name TEXT NOT NULL, category_id TEXT);
            INSERT INTO product VALUES ('08901719134845', 'Parle-G', 'biscuits');
            """;
        command.ExecuteNonQuery();
    }

    private AnalysisRepository Repository() =>
        new(new CatalogueConnection(new CataloguePaths(new TempStorage(_directory))));

    private sealed class TempStorage(string directory) : ICatalogueStorage
    {
        public string DataDirectory => directory;

        public Task<Stream?> OpenBundledCatalogueAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(null);
    }

    [Fact]
    public async Task A_catalogue_with_no_dictionary_yields_an_empty_one_rather_than_throwing()
    {
        var dictionary = await Repository().LoadDictionaryAsync(TestContext.Current.CancellationToken);

        Assert.Empty(dictionary);
    }

    [Fact]
    public async Task A_catalogue_with_no_peer_statistics_offers_no_comparison_rather_than_throwing()
    {
        var stats = await Repository().PeerStatsAsync(
            "biscuits", NutritionBasis.Per100g, TestContext.Current.CancellationToken);

        Assert.Empty(stats);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }
}
