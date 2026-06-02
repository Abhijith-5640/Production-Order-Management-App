namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Performs the file swap when a new version is downloaded. Lives in
/// the Updater namespace at runtime; the abstraction is defined here so
/// <c>AppUpdater</c> can be unit-tested without touching the disk.
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// Returns the current installed version (read from <c>version.json</c>).
    /// </summary>
    string GetCurrentVersion();

    /// <summary>
    /// Stops the WinSW service, swaps the new files in (preserving
    /// <c>db_config.json</c> and <c>logs/</c>), and restarts the service.
    /// Throws on failure; the background service catches and logs.
    /// </summary>
    Task ApplyUpdateAsync(string zipPath, CancellationToken cancellationToken);
}
