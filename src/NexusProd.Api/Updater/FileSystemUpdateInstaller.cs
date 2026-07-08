using System.Text.Json;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Updater;

/// <summary>
/// Writes the downloaded package to <c>update-pending.zip</c> and signals the
/// NexusProd user-space launcher to restart by calling <c>Environment.Exit(100)</c>.
/// </summary>
public sealed class FileSystemUpdateInstaller : IUpdateInstaller
{
    private readonly ILogger<FileSystemUpdateInstaller> _logger;
    private readonly string _installDir;

    public FileSystemUpdateInstaller(
        ILogger<FileSystemUpdateInstaller> logger)
    {
        _logger = logger;
        _installDir = AppContext.BaseDirectory;
    }

    public string GetCurrentVersion()
    {
        try
        {
            var path = Path.Combine(_installDir, "version.json");
            if (!File.Exists(path)) return "1.0.0";
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() ?? "1.0.0" : "1.0.0";
        }
        catch
        {
            return "1.0.0";
        }
    }

    public async Task ApplyUpdateAsync(string zipPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Update package not found", zipPath);

        _logger.LogInformation(
            "Applying update from {Zip} in {Dir}",
            zipPath, _installDir);

        var pendingZip = Path.Combine(_installDir, "update-pending.zip");

        // The zip is already at the correct location (downloaded by AppUpdater)
        // No copy needed - just signal the launcher
        _logger.LogInformation("Update package ready at {PendingZip}. Signaling launcher to restart.", pendingZip);

        // Wait 1 second for logs to flush
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

        // Signal the launcher to restart us
        _logger.LogWarning("Exiting with code 100 to trigger launcher restart...");
        Environment.Exit(100);
    }
}
