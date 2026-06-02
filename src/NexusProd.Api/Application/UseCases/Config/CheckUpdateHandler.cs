using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Config;

/// <summary>
/// Triggers an immediate update check on the <c>AppUpdater</c> background
/// service and returns the current status. The actual work runs in the
/// background; the handler just pokes the state and returns.
/// </summary>
public sealed record CheckUpdateCommand();
public sealed record CheckUpdateResult(bool Accepted, string Message);

public sealed class CheckUpdateHandler : IHandler<CheckUpdateCommand, CheckUpdateResult>
{
    private readonly IUpdateState _state;
    private readonly IUpdateTrigger _trigger;

    public CheckUpdateHandler(IUpdateState state, IUpdateTrigger trigger)
    {
        _state = state;
        _trigger = trigger;
    }

    public Task<Result<CheckUpdateResult>> HandleAsync(CheckUpdateCommand request, CancellationToken cancellationToken)
    {
        _state.Set(new UpdateStatus(UpdatePhase.Checking, "Manual check requested", _state.Current.LatestVersion, DateTimeOffset.UtcNow));
        _trigger.RequestCheck();
        return Task.FromResult(Result<CheckUpdateResult>.Success(new CheckUpdateResult(true, "Update check started")));
    }
}
