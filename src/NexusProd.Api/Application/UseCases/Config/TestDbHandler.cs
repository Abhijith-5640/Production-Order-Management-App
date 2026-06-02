using MySqlConnector;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Config;

public sealed record TestDbCommand(string Host, int Port, string User, string Password, string Database);

public sealed class TestDbHandler : IHandler<TestDbCommand, string>
{
    public async Task<Result<string>> HandleAsync(TestDbCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.Database))
            return Error.InvalidInput("Host and Database are required");

        try
        {
            var connectionString = $"Server={request.Host};Port={request.Port};User ID={request.User};Password={request.Password};Database={request.Database};Connection Timeout=5;";
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            await cmd.ExecuteScalarAsync(cancellationToken);
            return "Database connection successful!";
        }
        catch (Exception ex)
        {
            return Error.DatabaseError(ex.Message);
        }
    }
}
