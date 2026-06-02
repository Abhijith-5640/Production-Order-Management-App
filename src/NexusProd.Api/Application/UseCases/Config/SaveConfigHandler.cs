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

    public SaveConfigHandler(IDbConfigStore store) => _store = store;

    public async Task<Result<string>> HandleAsync(SaveConfigCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Database))
            return Error.InvalidInput("Host and Database are required");

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
            return Error.ConfigurationError("Failed to write configuration file: " + ex.Message);
        }
    }
}
