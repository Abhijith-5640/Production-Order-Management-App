using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Infrastructure.Configuration;
using NexusProd.Api.Infrastructure;

namespace NexusProd.Api.Updater;

/// <summary>
/// Long-running background service. Polls the update server on the
/// configured interval, downloads a new package when one is available,
/// then triggers <see cref="IUpdateInstaller.ApplyUpdateAsync"/>.
///
/// The install step is what actually swaps files and restarts the
/// WinSW service — see the comment on <see cref="FileSystemUpdateInstaller"/>.
/// </summary>
public sealed class AppUpdater : BackgroundService
{
    private readonly IUpdateServer _server;
    private readonly IUpdateInstaller _installer;
    private readonly IUpdateState _state;
    private readonly IUpdateTrigger _trigger;
    private readonly UpdateServerSettings _settings;
    private readonly ILogger<AppUpdater> _logger;
    private readonly string _installDir;

    public AppUpdater(
        IUpdateServer server,
        IUpdateInstaller installer,
        IUpdateState state,
        IUpdateTrigger trigger,
        IOptions<UpdateServerSettings> settings,
        ILogger<AppUpdater> logger)
    {
        _server = server;
        _installer = installer;
        _state = state;
        _trigger = trigger;
        _settings = settings.Value;
        _logger = logger;
        _installDir = AppContext.BaseDirectory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wire the manual trigger — the API layer pokes it when the
        // user clicks "Check for updates".
        if (_trigger is InMemoryUpdateTrigger concrete)
        {
            concrete.OnTrigger += () => _ = CheckAndApplyAsync(stoppingToken);
        }

        if (!_settings.Enabled)
        {
            _logger.LogInformation("Auto-update disabled. Set UpdateServerSettings:Enabled=true to enable.");
            _state.Set(new UpdateStatus(UpdatePhase.Idle, "Update check disabled", null, null));
            return;
        }

        _state.Set(new UpdateStatus(UpdatePhase.Idle, "Waiting for first check", null, null));

        // Stagger the first check by 30 seconds so the app finishes booting.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (TaskCanceledException) { return; }

        // Then loop on the configured interval.
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.CheckIntervalMinutes));
        do
        {
            await CheckAndApplyAsync(stoppingToken);
        }
        while (await SafeWaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task CheckAndApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            _state.Set(new UpdateStatus(UpdatePhase.Checking, "Checking for updates…", _state.Current.LatestVersion, DateTimeOffset.UtcNow));
            var info = await _server.GetLatestVersionAsync(cancellationToken);
            if (info is null)
            {
                _state.Set(new UpdateStatus(UpdatePhase.Idle, "No update server reachable", _state.Current.LatestVersion, DateTimeOffset.UtcNow));
                return;
            }

            var current = _installer.GetCurrentVersion();
            if (string.CompareOrdinal(info.LatestVersion, current) <= 0)
            {
                _state.Set(new UpdateStatus(UpdatePhase.Idle, $"Up to date (v{current})", info.LatestVersion, DateTimeOffset.UtcNow));
                return;
            }

            _state.Set(new UpdateStatus(UpdatePhase.Downloading, $"Downloading v{info.LatestVersion}…", info.LatestVersion, DateTimeOffset.UtcNow));
            var pendingPath = Path.Combine(_installDir, "update-pending.zip");
            await _server.DownloadPackageAsync(info.DownloadUrl, pendingPath, cancellationToken);

            _state.Set(new UpdateStatus(UpdatePhase.Applying, "Applying update…", info.LatestVersion, DateTimeOffset.UtcNow));
            await _installer.ApplyUpdateAsync(pendingPath, cancellationToken);

            _state.Set(new UpdateStatus(UpdatePhase.Succeeded, $"Updated to v{info.LatestVersion}", info.LatestVersion, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-update failed");
            _state.Set(new UpdateStatus(UpdatePhase.Failed, ex.Message, _state.Current.LatestVersion, DateTimeOffset.UtcNow));
        }
    }
}
