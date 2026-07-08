using System.Diagnostics;
using System.IO.Compression;
using System.Text;

/// <summary>
/// User-space bootstrapper launcher for NexusProd.
/// Runs in the background without a visible window, starts the API process,
/// monitors its lifetime, and handles auto-updates via the update-pending.zip mechanism.
/// </summary>

var installDir = AppContext.BaseDirectory;
var logDir = Path.Combine(installDir, "logs");
var launcherLog = Path.Combine(logDir, "launcher.log");
var apiLog = Path.Combine(logDir, "api.log");

try { Directory.CreateDirectory(logDir); } catch { /* best-effort */ }

// Thread-safe, auto-rolling file logger
object _logLock = new();
const long MaxLogBytes = 10 * 1024 * 1024; // 10MB

void Log(string message)
{
    try
    {
        lock (_logLock)
        {
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Launcher] {message}";
            RollLogIfNeeded(launcherLog);
            File.AppendAllText(launcherLog, line + Environment.NewLine);
        }
    }
    catch { /* swallow logging failures */ }
}

void RollLogIfNeeded(string path)
{
    try
    {
        if (!File.Exists(path)) return;
        var fi = new FileInfo(path);
        if (fi.Length < MaxLogBytes) return;

        var oldPath = path + ".old";
        try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { }
        File.Move(path, oldPath);
    }
    catch { /* swallow */ }
}

void LogApiLine(string line, bool isError)
{
    try
    {
        lock (_logLock)
        {
            RollLogIfNeeded(apiLog);
            var prefix = isError ? "[API-ERR]" : "[API]";
            File.AppendAllText(apiLog, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] {prefix} {line}{Environment.NewLine}");
        }
    }
    catch { /* swallow */ }
}

// ── Helper: run a detached cmd to perform file operations ───────────────────
void DetachedDelete(string path)
{
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c del /F \"{path}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit();
    }
    catch { /* best-effort */ }
}

Log("========================================");
Log("NexusProd Launcher starting.");
Log($"Install dir: {installDir}");

// ── Crash tracking helpers ────────────────────────────────────────────────
var crashLogPath = Path.Combine(logDir, "crash-count.txt");

int GetCrashCount()
    => int.TryParse(File.Exists(crashLogPath) ? File.ReadAllText(crashLogPath).Trim() : "", out var c) ? c : 0;

void IncrementCrashCount()
    { try { File.WriteAllText(crashLogPath, GetCrashCount().ToString()); } catch { } }

void ResetCrashCount()
    { try { if (File.Exists(crashLogPath)) File.Delete(crashLogPath); } catch { } }

DateTime? GetFirstCrashTime()
{
    var path = Path.Combine(logDir, "first-crash.txt");
    return DateTime.TryParse(File.Exists(path) ? File.ReadAllText(path).Trim() : "", out var t) ? t : null;
}

void SetFirstCrashTime()
{
    var path = Path.Combine(logDir, "first-crash.txt");
    try { File.WriteAllText(path, DateTime.UtcNow.ToString("O")); } catch { }
}

void ClearFirstCrashTime()
{
    var path = Path.Combine(logDir, "first-crash.txt");
    try { if (File.Exists(path)) File.Delete(path); } catch { }
}

// ── Constants ────────────────────────────────────────────────────────────
const int ExitCodeUpdatePending = 100;
const int ExitCodeGracefulShutdown = 0;
const int MaxConsecutiveCrashes = 5;
var watch = System.Diagnostics.Stopwatch.StartNew();

// ── Main launcher loop ───────────────────────────────────────────────────
while (true)
{
    // ── Check for pending update (NexusProd.exe.new) on every loop iteration ───
    var newExePath = Path.Combine(installDir, "NexusProd.exe.new");
    if (File.Exists(newExePath))
    {
        Log("Found NexusProd.exe.new — applying update...");

        var currentExe = Path.Combine(installDir, "NexusProd.exe");
        var backupExe = Path.Combine(installDir, "NexusProd.exe.bak");

        try
        {
            // Backup current
            if (File.Exists(currentExe))
            {
                if (File.Exists(backupExe)) File.Delete(backupExe);
                File.Move(currentExe, backupExe);
            }

            // Move new to current
            File.Move(newExePath, currentExe);

            // Start the new version and exit
            Log("Starting updated NexusProd.exe...");
            var startInfo = new ProcessStartInfo
            {
                FileName = currentExe,
                UseShellExecute = true
            };
            Process.Start(startInfo);
            Log("Update complete. Exiting old launcher.");
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Log($"ERROR applying .new update: {ex.Message}. Restoring backup.");
            try
            {
                if (File.Exists(backupExe)) File.Move(backupExe, currentExe, overwrite: true);
            }
            catch { }
        }
    }

    // ── Check for update-pending.zip on every loop iteration ───────────────────
    var pendingZip = Path.Combine(installDir, "update-pending.zip");
    if (File.Exists(pendingZip))
    {
        Log("Found update-pending.zip — extracting update...");

        // Wait for handles to clear
        Thread.Sleep(TimeSpan.FromSeconds(1));

        var extractDir = Path.Combine(Path.GetTempPath(), $"nexusprod-update-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(pendingZip, extractDir);

            // Copy files, handling locked NexusProd.Api.exe
            foreach (var file in Directory.GetFiles(extractDir, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(extractDir, file);
                var destPath = Path.Combine(installDir, relativePath);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    // If this is NexusProd.Api.exe, write as .new to avoid locked-file issues
                    if (string.Equals(relativePath, "NexusProd.Api.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(file, destPath + ".new", overwrite: true);
                        Log($"Copied NexusProd.Api.exe as .new");
                    }
                    else
                    {
                        File.Copy(file, destPath, overwrite: true);
                    }
                }
                catch (IOException ex)
                {
                    Log($"Skipped locked file: {relativePath} ({ex.Message})");
                }
            }

            Log("Update extraction complete.");
        }
        catch (Exception ex)
        {
            Log($"ERROR extracting update: {ex.Message}");
        }
        finally
        {
            try { Directory.Delete(extractDir, recursive: true); } catch { }
        }

        // Clean up the zip
        DetachedDelete(pendingZip);
        try { File.Delete(pendingZip); } catch { }
    }

    // ── Start NexusProd.Api.exe ───────────────────────────────────────────────
    var apiExe = Path.Combine(installDir, "NexusProd.Api.exe");
    if (!File.Exists(apiExe))
    {
        Log($"ERROR: NexusProd.Api.exe not found in {installDir}. Waiting for file...");
        Thread.Sleep(TimeSpan.FromSeconds(10));
        continue;
    }

    // Check crash window (60 seconds)
    var firstCrash = GetFirstCrashTime();
    if (firstCrash.HasValue && (DateTime.UtcNow - firstCrash.Value).TotalSeconds > 60)
    {
        Log("60-second crash window expired. Resetting crash counter.");
        ResetCrashCount();
        ClearFirstCrashTime();
    }

    var crashCount = GetCrashCount();
    if (crashCount >= MaxConsecutiveCrashes)
    {
        Log($"FATAL: Reached {MaxConsecutiveCrashes} consecutive crashes within 60 seconds. Shutting down launcher.");
        Log("Manual intervention required. Delete logs/ first to reset.");
        break;
    }

    Log($"Starting NexusProd.Api.exe (crash #{crashCount + 1}/{MaxConsecutiveCrashes})...");

    var psi = new ProcessStartInfo
    {
        FileName = apiExe,
        Arguments = "--urls=http://0.0.0.0:5099",
        WorkingDirectory = installDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var apiProcess = Process.Start(psi);
    if (apiProcess == null)
    {
        Log("ERROR: Failed to start NexusProd.Api.exe process.");
        Thread.Sleep(TimeSpan.FromSeconds(5));
        continue;
    }

    // Stream stdout
    var stdoutTask = Task.Run(async () =>
    {
        try
        {
            while (!apiProcess.StandardOutput.EndOfStream)
            {
                var line = await apiProcess.StandardOutput.ReadLineAsync();
                if (line != null) LogApiLine(line, isError: false);
            }
        }
        catch { }
    });

    // Stream stderr
    var stderrTask = Task.Run(async () =>
    {
        try
        {
            while (!apiProcess.StandardError.EndOfStream)
            {
                var line = await apiProcess.StandardError.ReadLineAsync();
                if (line != null) LogApiLine(line, isError: true);
            }
        }
        catch { }
    });

    // Wait for API to exit
    apiProcess.WaitForExit();
    var exitCode = apiProcess.ExitCode;

    // Drain output streams
    try { await stdoutTask; } catch { }
    try { await stderrTask; } catch { }

    Log($"NexusProd.Api.exe exited with code {exitCode} after {watch.Elapsed.TotalSeconds:F1}s.");

    if (exitCode == ExitCodeGracefulShutdown)
    {
        Log("Graceful shutdown requested. Exiting launcher.");
        break;
    }
    else if (exitCode == ExitCodeUpdatePending)
    {
        Log("Update pending signal received. Looping to apply update...");
        continue; // Go back to top of loop to check for update-pending.zip
    }
    else
    {
        // Crash - increment and wait
        IncrementCrashCount();
        if (GetCrashCount() == 1)
        {
            SetFirstCrashTime();
        }

        Log($"Crash detected. Waiting 3 seconds before restart...");
        Thread.Sleep(TimeSpan.FromSeconds(3));
    }
}

Log("NexusProd Launcher stopped.");
