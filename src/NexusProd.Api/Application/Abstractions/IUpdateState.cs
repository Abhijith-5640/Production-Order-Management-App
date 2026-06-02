namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// In-memory state for the auto-update flow. Read by
/// <c>GET /api/updater/status</c> so the Login page can show progress.
/// </summary>
public interface IUpdateState
{
    UpdateStatus Current { get; }
    void Set(UpdateStatus status);
}

public enum UpdatePhase
{
    Idle,
    Checking,
    Downloading,
    Applying,
    Succeeded,
    Failed
}

public sealed record UpdateStatus(UpdatePhase Phase, string? Message, string? LatestVersion, DateTimeOffset? LastChecked)
{
    public static readonly UpdateStatus Initial = new(UpdatePhase.Idle, "Idle", null, null);
}
