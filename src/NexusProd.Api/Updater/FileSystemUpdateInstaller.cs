using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Updater;

/// <summary>
/// Stops the WinSW service, swaps the new files in (preserving
/// <c>db_config.json</c> and <c>logs/</c>), and restarts the service.
///
/// Production sequence:
///   1. Run <c>net stop NexusProd</c> (WinSW)
///   2. Unzip <c>update-pending.zip</c> over the install dir
///   3. Update <c>version.json</c>
///   4. Run <c>net start NexusProd</c>
///
/// For the POC we keep the install path configurable via env var
/// <c>NEXUSPROD_WINSW_NAME</c> and skip the actual WinSW calls when
/// running interactively (e.g. <c>dotnet run</c>).
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

    public async Task ApplyUpdateAsync(string zipPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Update package not found", zipPath);

        var winswName = Environment.GetEnvironmentVariable("NEXUSPROD_WINSW_NAME") ?? "NexusProd";
        var interactive = _env.IsDevelopment() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXUSPROD_WINSW_NAME"));

        _logger.LogInformation("Applying update from {Zip} in {Dir} (interactive={Interactive})",
            zipPath, _installDir, interactive);

        if (!interactive)
        {
            await RunServiceCommand($"net stop {winswName}", cancellationToken);
        }

        // Preserve runtime data — copy out, then back over the new files.
        var preservedConfig = Path.Combine(Path.GetTempPath(), "db_config.json");
        var preservedLogs = Path.Combine(Path.GetTempPath(), "logs");
        try
        {
            var cfgSrc = Path.Combine(_installDir, "db_config.json");
            if (File.Exists(cfgSrc)) File.Copy(cfgSrc, preservedConfig, overwrite: true);
            var logsSrc = Path.Combine(_installDir, "logs");
            if (Directory.Exists(logsSrc))
            {
                if (Directory.Exists(preservedLogs)) Directory.Delete(preservedLogs, recursive: true);
                Directory.CreateDirectory(preservedLogs);
                foreach (var f in Directory.GetFiles(logsSrc, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(preservedLogs, Path.GetRelativePath(logsSrc, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest, overwrite: true);
                }
            }

            ZipFile.ExtractToDirectory(zipPath, _installDir, overwriteFiles: true);

            if (File.Exists(preservedConfig)) File.Copy(preservedConfig, Path.Combine(_installDir, "db_config.json"), overwrite: true);
            if (Directory.Exists(preservedLogs))
            {
                var logsDst = Path.Combine(_installDir, "logs");
                Directory.CreateDirectory(logsDst);
                foreach (var f in Directory.GetFiles(preservedLogs, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(logsDst, Path.GetRelativePath(preservedLogs, f));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(f, dest, overwrite: true);
                }
            }
        }
        finally
        {
            // Best-effort cleanup.
            try { if (File.Exists(preservedConfig)) File.Delete(preservedConfig); } catch { }
            try { if (Directory.Exists(preservedLogs)) Directory.Delete(preservedLogs, recursive: true); } catch { }
            try { File.Delete(zipPath); } catch { }
        }

        if (!interactive)
        {
            await RunServiceCommand($"net start {winswName}", cancellationToken);
        }
    }

    private async Task RunServiceCommand(string command, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync(cancellationToken);
            _logger.LogInformation("Executed: {Command} → exit {Code}", command, p.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service command failed: {Command}", command);
            throw;
        }
    }
}
