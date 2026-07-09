using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Config;

public sealed class GetConfigStatusHandler : IHandler<GetConfigStatusQuery, GetConfigStatusResult>
{
    private readonly IDbConfigStore _store;
    private readonly ILogger<GetConfigStatusHandler> _logger;

    public GetConfigStatusHandler(IDbConfigStore store, ILogger<GetConfigStatusHandler> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<Result<GetConfigStatusResult>> HandleAsync(GetConfigStatusQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _store.ReadAsync(cancellationToken);

            if (snapshot is null)
            {
                return new GetConfigStatusResult(
                    Configured: false,
                    Host: string.Empty,
                    Port: 3306,
                    Database: string.Empty,
                    User: string.Empty,
                    Password: string.Empty);
            }

            return new GetConfigStatusResult(
                Configured: true,
                Host: snapshot.Config.Host,
                Port: snapshot.Config.Port,
                Database: snapshot.Config.Database,
                User: snapshot.Config.User,
                Password: snapshot.Config.Password);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetConfigStatus failed");
            return Error.ConfigurationError("Failed to read server configuration: " + ex.Message);
        }
    }
}
