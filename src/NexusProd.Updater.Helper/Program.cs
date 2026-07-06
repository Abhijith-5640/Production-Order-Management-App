using System.Diagnostics;
using System.IO.Compression;
using System.Threading;

// Usage: NexusProd.Updater.Helper <zipPath> <installDir> <winswServiceName> <parentPid>
if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: NexusProd.Updater.Helper <zipPath> <installDir> <winswServiceName> <parentPid>");
    Environment.Exit(1);
}

var zipPath = args[0];
var installDir = args[1];
var winswServiceName = args[2];
var parentPidArg = args[3];

if (!int.TryParse(parentPidArg, out var parentPid))
{
    Console.Error.WriteLine($"Invalid parent PID: {parentPidArg}");
    Environment.Exit(1);
}

var logDir = Path.Combine(installDir, "logs");
var logFile = Path.Combine(logDir, "updater-helper.log");

try { Directory.CreateDirectory(logDir); } catch { /* best-effort */ }

void Log(string message)
{
    try
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] {message}";
        File.AppendAllText(logFile, line + Environment.NewLine);
    }
    catch { /* swallow logging failures */ }
}

try
{
    Log($"Updater helper started — zip={zipPath}, installDir={installDir}, service={winswServiceName}, parentPid={parentPid}");

    // ── Step 3: Wait for parent process to exit ──────────────────────────
    try
    {
        using var parent = Process.GetProcessById(parentPid);
        Log($"Waiting for parent process {parentPid} to exit (timeout 30s)…");
        parent.WaitForExit(TimeSpan.FromSeconds(30));
        Log($"Parent process {parentPid} has exited.");
    }
    catch (ArgumentException)
    {
        Log($"Parent process {parentPid} already gone — proceeding.");
    }
    catch (Exception ex)
    {
        Log($"Parent wait warning: {ex.Message} — proceeding anyway.");
    }

    // ── Step 4: net stop ─────────────────────────────────────────────────
    Log($"Running: net stop {winswServiceName}");
    var stopResult = RunCommand($"net stop {winswServiceName}");
    Log($"net stop → exit {stopResult.ExitCode}; stdout: {stopResult.StdOut}; stderr: {stopResult.StdErr}");

    // ── Step 5: Preserve runtime files ──────────────────────────────────
    var preservedConfig = Path.Combine(Path.GetTempPath(), "db_config.json");
    var preservedLocal = Path.Combine(Path.GetTempPath(), "appsettings.local.json");
    var preservedLogs = Path.Combine(Path.GetTempPath(), "logs");

    var cfgSrc = Path.Combine(installDir, "db_config.json");
    if (File.Exists(cfgSrc))
    {
        File.Copy(cfgSrc, preservedConfig, overwrite: true);
        Log($"Preserved db_config.json");
    }

    var localSrc = Path.Combine(installDir, "appsettings.local.json");
    if (File.Exists(localSrc))
    {
        File.Copy(localSrc, preservedLocal, overwrite: true);
        Log($"Preserved appsettings.local.json");
    }

    var logsSrc = Path.Combine(installDir, "logs");
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
        Log($"Preserved logs/ ({Directory.GetFiles(preservedLogs, "*", SearchOption.AllDirectories).Length} files)");
    }

    // ── Step 6: Extract with retry ──────────────────────────────────────
    const int maxAttempts = 5;
    var extracted = false;
    for (var attempt = 1; attempt <= maxAttempts; attempt++)
    {
        try
        {
            ZipFile.ExtractToDirectory(zipPath, installDir, overwriteFiles: true);
            extracted = true;
            Log($"Extracted update package (attempt {attempt}/{maxAttempts}).");
            break;
        }
        catch (IOException ex)
        {
            Log($"Extract attempt {attempt}/{maxAttempts} failed (IO): {ex.Message}. Retrying in 1s…");
            if (attempt < maxAttempts) Thread.Sleep(TimeSpan.FromSeconds(1));
        }
        catch (Exception ex)
        {
            Log($"Extract attempt {attempt}/{maxAttempts} failed: {ex.Message}. Retrying in 1s…");
            if (attempt < maxAttempts) Thread.Sleep(TimeSpan.FromSeconds(1));
        }
    }

    if (!extracted)
    {
        throw new IOException($"Failed to extract update package after {maxAttempts} attempts.");
    }

    // ── Step 7: Restore preserved files ─────────────────────────────────
    if (File.Exists(preservedConfig))
    {
        File.Copy(preservedConfig, Path.Combine(installDir, "db_config.json"), overwrite: true);
        Log($"Restored db_config.json");
    }

    if (File.Exists(preservedLocal))
    {
        File.Copy(preservedLocal, Path.Combine(installDir, "appsettings.local.json"), overwrite: true);
        Log($"Restored appsettings.local.json");
    }

    if (Directory.Exists(preservedLogs))
    {
        var logsDst = Path.Combine(installDir, "logs");
        Directory.CreateDirectory(logsDst);
        foreach (var f in Directory.GetFiles(preservedLogs, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(logsDst, Path.GetRelativePath(preservedLogs, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, overwrite: true);
        }
        Log($"Restored logs/");
    }

    // ── Step 8: Cleanup ────────────────────────────────────────────────
    try { if (File.Exists(preservedConfig)) File.Delete(preservedConfig); } catch { }
    try { if (File.Exists(preservedLocal)) File.Delete(preservedLocal); } catch { }
    try { if (Directory.Exists(preservedLogs)) Directory.Delete(preservedLogs, recursive: true); } catch { }
    try { File.Delete(zipPath); } catch { }
    Log("Cleaned up temp files.");

    // ── Step 9: net start ──────────────────────────────────────────────
    Log($"Running: net start {winswServiceName}");
    var startResult = RunCommand($"net start {winswServiceName}");
    Log($"net start → exit {startResult.ExitCode}; stdout: {startResult.StdOut}; stderr: {startResult.StdErr}");

    Log("Update applied successfully. Helper exiting with code 0.");
    Environment.Exit(0);
}
catch (Exception ex)
{
    Log($"FATAL ERROR: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

    // ── Step 10 (error path): best-effort net start ───────────────────
    try
    {
        Log($"Best-effort net start {winswServiceName} after failure…");
        var recoveryResult = RunCommand($"net start {winswServiceName}");
        Log($"Recovery net start → exit {recoveryResult.ExitCode}; stdout: {recoveryResult.StdOut}; stderr: {recoveryResult.StdErr}");
    }
    catch (Exception recoveryEx)
    {
        Log($"Recovery net start also failed: {recoveryEx.Message}");
    }

    Log("Helper exiting with code 1.");
    Environment.Exit(1);
}

// ── Helper: run a command and capture output ──────────────────────────────
// Reads stdout/stderr BEFORE WaitForExit to avoid pipe-buffer deadlocks.
static (int ExitCode, string StdOut, string StdErr) RunCommand(string command)
{
    var psi = new ProcessStartInfo("cmd.exe", $"/c {command}")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    using var p = Process.Start(psi)!;

    var stdoutTask = p.StandardOutput.ReadToEndAsync();
    var stderrTask = p.StandardError.ReadToEndAsync();
    p.WaitForExit();

    var stdout = stdoutTask.Result;
    var stderr = stderrTask.Result;

    return (p.ExitCode, stdout.Trim(), stderr.Trim());
}
