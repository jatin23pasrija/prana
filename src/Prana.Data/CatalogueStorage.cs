namespace Prana.Data;

/// <summary>
/// Where the app keeps its databases, and how it reaches the catalogue bundled inside the
/// installed package.
/// </summary>
/// <remarks>
/// This is an interface because the two halves it hides are the parts that cannot be tested off
/// a device. An Android asset is not a file: it lives inside the APK and has to be opened
/// through the platform and copied out before SQLite can touch it. Putting that behind an
/// interface means every repository, migration and installer below can be tested against a
/// temporary directory on a build machine, and only this one implementation needs a phone.
/// </remarks>
public interface ICatalogueStorage
{
    /// <summary>Directory the app may write to. Databases live here.</summary>
    string DataDirectory { get; }

    /// <summary>
    /// Opens the catalogue bundled in the installed package, or returns null when the app was
    /// built without one. A missing bundle is a supported configuration, not a fault: ADR-0030
    /// has a flavour that ships no catalogue at all and downloads one instead.
    /// </summary>
    Task<Stream?> OpenBundledCatalogueAsync(CancellationToken cancellationToken);
}

/// <summary>The paths the app uses, derived from one directory so nothing hard-codes a layout.</summary>
public sealed class CataloguePaths(ICatalogueStorage storage)
{
    /// <summary>
    /// The installed catalogue. Read-only at runtime and replaced wholesale by sync, which is
    /// only safe because no user data lives in it. See ADR-0007.
    /// </summary>
    public string Catalogue => Path.Combine(storage.DataDirectory, "catalogue.db");

    /// <summary>
    /// Grocery list, scan history, settings. Never touched by sync, never replaced, and the
    /// reason the catalogue can be thrown away without losing anything the user created.
    /// </summary>
    public string User => Path.Combine(storage.DataDirectory, "user.db");

    /// <summary>Where a downloaded catalogue is verified before it is allowed to become the real one.</summary>
    public string Staging => Path.Combine(storage.DataDirectory, "catalogue.incoming.db");
}
