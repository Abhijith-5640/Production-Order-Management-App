using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Config;

public sealed record SaveConfigCommand(
    string Host,
    int Port,
    string User,
    string Password,
    string Database,
    bool? UseMockDb);

public sealed class SaveConfigHandler : IHandler<SaveConfigCommand, string>
{
    private readonly IDbConfigStore _store;
    private readonly ILogger<SaveConfigHandler> _logger;

    public SaveConfigHandler(IDbConfigStore store, ILogger<SaveConfigHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<Result<string>> HandleAsync(SaveConfigCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Database))
            return Error.InvalidInput("Host and Database are required");

        try
        {
            var current = await _store.ReadAsync(cancellationToken);
            var merged = new DbConfigSnapshot(
                request.UseMockDb ?? current?.UseMockDb ?? false,
                new DbConfig(
                    request.Host,
                    request.Port,
                    request.User,
                    request.Password,
                    request.Database));

            try
            {
                await _store.WriteAsync(merged, cancellationToken);
                return "Configuration saved successfully. Please restart server if needed.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveConfig failed while writing config file for host {Host}", request.Host);
                return Error.ConfigurationError("Failed to write configuration file: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveConfig failed while reading existing config for host {Host}", request.Host);
            return Error.ConfigurationError("Failed to read existing configuration: " + ex.Message);
        }
    }
}
