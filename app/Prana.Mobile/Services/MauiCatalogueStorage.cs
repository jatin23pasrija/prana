using Prana.Data;

namespace Prana.Mobile.Services;

/// <summary>
/// The one piece of the data layer that needs a phone.
/// </summary>
/// <remarks>
/// Everything else in Prana.Data works against a plain directory and is tested on a build
/// machine. This class exists because an Android asset is not a file: it lives inside the
/// installed package and can only be reached through the platform, which is exactly the thing a
/// unit test cannot do.
/// </remarks>
public sealed class MauiCatalogueStorage : ICatalogueStorage
{
    /// <summary>
    /// The bundled starter catalogue, Brotli compressed. Named here rather than in Prana.Data so
    /// the packaging detail stays with the package.
    /// </summary>
    private const string BundledCatalogue = "catalogue-starter.db.br";

    public string DataDirectory => FileSystem.Current.AppDataDirectory;

    public async Task<Stream?> OpenBundledCatalogueAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Copied into memory rather than handed over directly. The platform stream is not
            // seekable on every Android version, and decompressing over an awkward stream fails
            // in ways that are tedious to diagnose on a device.
            await using var packaged = await FileSystem.Current.OpenAppPackageFileAsync(BundledCatalogue);

            var buffer = new MemoryStream();
            await packaged.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            return buffer;
        }
        catch (FileNotFoundException)
        {
            // A build flavour that ships no catalogue. ADR-0030 makes this a supported
            // configuration, so it returns null rather than throwing.
            return null;
        }
    }
}
