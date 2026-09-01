using System.IO.Compression;

namespace Prana.Data;

/// <summary>What an install attempt did, so the caller can say something true about it.</summary>
public enum InstallOutcome
{
    /// <summary>A usable catalogue was already installed. Nothing was touched.</summary>
    AlreadyInstalled,

    /// <summary>The bundled catalogue was unpacked and is now installed.</summary>
    InstalledFromBundle,

    /// <summary>This build ships no bundled catalogue. One has to be downloaded.</summary>
    NoBundle,

    /// <summary>A bundle was present but unusable. The app continues without a catalogue.</summary>
    BundleUnusable,
}

/// <summary>
/// Puts the bundled catalogue in place on first run.
/// </summary>
/// <remarks>
/// An Android asset is not a file. It lives inside the APK, cannot be opened by SQLite in place,
/// and has to be copied out. Since it has to be copied anyway, it is shipped Brotli compressed:
/// 0.4 MB in the package instead of 2.4 MB, paid back once with a decompression that happens
/// while the first screen is already on show.
///
/// Installation goes through a staging file and a rename. A first run interrupted halfway must
/// leave no catalogue rather than half of one, because a half-written database that happens to
/// open is far worse than none.
/// </remarks>
public sealed class CatalogueInstaller(
    ICatalogueStorage storage,
    CataloguePaths paths,
    CatalogueConnection catalogue)
{
    public async Task<InstallOutcome> EnsureInstalledAsync(CancellationToken cancellationToken)
    {
        if (catalogue.ReadStatus().IsInstalled)
        {
            return InstallOutcome.AlreadyInstalled;
        }

        // Anything already at the destination failed the check above, so it is not usable.
        Delete(paths.Catalogue);
        Delete(paths.Staging);

        await using var bundled = await storage.OpenBundledCatalogueAsync(cancellationToken);

        if (bundled is null)
        {
            return InstallOutcome.NoBundle;
        }

        try
        {
            Directory.CreateDirectory(storage.DataDirectory);

            await using (var staging = File.Create(paths.Staging))
            await using (var decompressed = new BrotliStream(bundled, CompressionMode.Decompress))
            {
                await decompressed.CopyToAsync(staging, cancellationToken);
            }

            if (!CatalogueConnection.IsUsable(paths.Staging))
            {
                Delete(paths.Staging);
                return InstallOutcome.BundleUnusable;
            }

            // The rename is the moment the catalogue exists. Everything before it is disposable.
            File.Move(paths.Staging, paths.Catalogue, overwrite: true);

            return InstallOutcome.InstalledFromBundle;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A corrupt bundle, a full disk, or a cancelled first run. None of these justify
            // failing to start, and all of them leave the app in the same state as a build with
            // no bundle at all.
            Delete(paths.Staging);
            return InstallOutcome.BundleUnusable;
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Nothing useful to do. The caller finds out through the status check either way.
        }
    }
}
