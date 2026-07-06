using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Updater;

/// <summary>
/// Validates the update package and delegates the stop → swap → start
/// sequence to <c>NexusProd.Updater.Helper.exe</c>, which outlives this
/// process so the WinSW service can be stopped cleanly.
///
/// For the POC we keep the install path configurable via env var
/// <c>NEXUSPROD_WINSW_NAME</c> and skip the launch when running
/// interactively (e.g. <c>dotnet run</c>).
/// </summary>
public sealed class FileSystemUpdateInstaller : IUpdateInstaller
{
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<FileSystemUpdateInstaller> _logger;
    private readonly string _installDir;

    public FileSystemUpdateInstaller(IWebHostEnvironment env, ILogger<FileSystemUpdateInstaller> logger)
    {
        _env = env;
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

    public Task ApplyUpdateAsync(string zipPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Update package not found", zipPath);

        var winswName = Environment.GetEnvironmentVariable("NEXUSPROD_WINSW_NAME") ?? "NexusProd";
        var interactive = _env.IsDevelopment() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXUSPROD_WINSW_NAME"));

        _logger.LogInformation(
            "Applying update from {Zip} in {Dir} via helper (interactive={Interactive})",
            zipPath, _installDir, interactive);

        if (interactive)
        {
            _logger.LogWarning(
                "Skipping update launch: interactive/dev environment detected. "
                + "Set NEXUSPROD_WINSW_NAME to a non-empty value to enable.");
            return Task.CompletedTask;
        }

        var helperExe = Path.Combine(_installDir, "NexusProd.Updater.Helper.exe");
        if (!File.Exists(helperExe))
            throw new FileNotFoundException(
                $"Updater helper not found: {helperExe}. "
                + "Ensure NexusProd.Updater.Helper.exe is in the install directory.", helperExe);

        var parentPid = Environment.ProcessId;
        var args = $"\"{zipPath}\" \"{_installDir}\" {winswName} {parentPid}";

        _logger.LogInformation("Launching updater helper: {HelperExe} {Args}", helperExe, args);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = helperExe,
                Arguments = args,
                WorkingDirectory = _installDir,
                UseShellExecute = true,
                CreateNoWindow = true
            };

            // Detach from this process — the helper outlives us.
            _ = Process.Start(psi);

            _logger.LogInformation("Updater helper launched (PID {ParentPid}). Returning immediately.", parentPid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch updater helper: {HelperExe}", helperExe);
            throw;
        }

        return Task.CompletedTask;
    }
}
