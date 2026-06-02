namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Reads and writes the runtime database configuration. The default
/// implementation stores the file (<c>db_config.json</c>) next to the
/// running exe so it remains editable without rebuilding.
/// </summary>
public interface IDbConfigStore
{
    /// <summary>Returns the current config, or null if the file is missing / unreadable.</summary>
    Task<DbConfigSnapshot?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Atomically replaces the current config on disk.</summary>
    Task WriteAsync(DbConfigSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>
/// Plain config record — Dapper and the connection factory both read from this.
/// </summary>
public sealed record DbConfigSnapshot(bool UseMockDb, DbConfig Config)
{
    public static DbConfigSnapshot Default() =>
        new(false, new DbConfig("localhost", 3306, "root", "admin@5555", "prod_app"));
}

public sealed record DbConfig(string Host, int Port, string User, string Password, string Database);
