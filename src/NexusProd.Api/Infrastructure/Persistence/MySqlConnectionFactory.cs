using MySqlConnector;
using NexusProd.Api.Application.Abstractions;

namespace NexusProd.Api.Infrastructure.Persistence;

/// <summary>
/// Hands out fresh <see cref="MySqlConnection"/> instances. Connections
/// are NOT pooled by the factory — the heavy lifting (pooling,
/// timeouts) is done by <c>MySqlConnector</c> under the hood when the
/// same connection string is reused.
/// </summary>
public sealed class MySqlConnectionFactory
{
    private readonly IDbConfigStore _store;

    public MySqlConnectionFactory(IDbConfigStore store) => _store = store;

    public async Task<MySqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _store.ReadAsync(cancellationToken)
            ?? throw new InvalidOperationException("db_config.json missing — set MySQL credentials via the Login page first.");
        if (snapshot.UseMockDb)
            throw new InvalidOperationException("Database is in mock mode — set use_mock_db=false in db_config.json");

        var cs = $"Server={snapshot.Config.Host};Port={snapshot.Config.Port};User ID={snapshot.Config.User};Password={snapshot.Config.Password};Database={snapshot.Config.Database};Pooling=true;Max Pool Size=100;Connection Idle Timeout=60;Connection Lifetime=300;";
        var conn = new MySqlConnection(cs);
        await conn.OpenAsync(cancellationToken);
        return conn;
    }

    public async Task<string> BuildConnectionStringAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _store.ReadAsync(cancellationToken)
            ?? throw new InvalidOperationException("db_config.json missing");
        return $"Server={snapshot.Config.Host};Port={snapshot.Config.Port};User ID={snapshot.Config.User};Password={snapshot.Config.Password};Database={snapshot.Config.Database};Pooling=true;Max Pool Size=100;Connection Idle Timeout=60;Connection Lifetime=300;";
    }
}
