using Prana.Data;

namespace Prana.Mobile.Services;

/// <summary>
/// Gets the databases ready, once, without blocking the first screen.
/// </summary>
/// <remarks>
/// Startup runs in the background and the home screen renders immediately, because the project
/// plan is explicit that opening the app must not wait on anything. On a first launch this
/// unpacks a 0.4 MB catalogue; on every later launch it does almost nothing.
///
/// Nothing here throws. A failure leaves the app running with no catalogue, which is a state it
/// already has to handle for the flavour that ships without one.
/// </remarks>
public sealed class CatalogueStartup(
    CatalogueInstaller installer,
    CatalogueConnection catalogue,
    UserDatabase user)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<CatalogueStatus>? _started;

    /// <summary>
    /// Runs startup, or waits for the run already in progress. Several screens ask for the
    /// catalogue at once on a cold start, and unpacking it twice would be a race over a file
    /// both are trying to create.
    /// </summary>
    public async Task<CatalogueStatus> EnsureReadyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            _started ??= RunAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return await _started;
    }

    private async Task<CatalogueStatus> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            user.Migrate();

            var outcome = await installer.EnsureInstalledAsync(cancellationToken);
            var status = catalogue.ReadStatus();

            if (outcome == InstallOutcome.InstalledFromBundle && status.IsInstalled)
            {
                user.RecordInstalledCatalogue(status, DateTimeOffset.UtcNow);
            }

            return status;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Out of space, or storage the app cannot write to. Neither is worth refusing to
            // start over, and the app already knows how to run with no catalogue.
            return CatalogueStatus.None;
        }
    }
}
