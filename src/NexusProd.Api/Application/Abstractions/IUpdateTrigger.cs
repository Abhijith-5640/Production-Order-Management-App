namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// In-process event the API uses to ask the background
/// <see cref="NexusProd.Api.Updater.AppUpdater"/> to run a check
/// immediately, instead of waiting for the next timer tick.
/// </summary>
public interface IUpdateTrigger
{
    /// <summary>Ask for an out-of-band update check.</summary>
    void RequestCheck();

    /// <summary>Raised when <see cref="RequestCheck"/> is called.</summary>
    event Action? OnTrigger;
}
