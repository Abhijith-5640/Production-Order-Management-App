using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Domain.Entities;
using NexusProd.Api.Infrastructure.Security;

namespace NexusProd.Api.Infrastructure.Persistence;

public sealed class MySqlUserRepository : IUserRepository
{
    private readonly MySqlConnectionFactory _factory;
    private readonly IPasswordHasher _hasher;
    private readonly ILogger<MySqlUserRepository> _logger;

    public MySqlUserRepository(
        MySqlConnectionFactory factory,
        IPasswordHasher hasher,
        ILogger<MySqlUserRepository> logger)
    {
        _factory = factory;
        _hasher = hasher;
        _logger = logger;
    }


    public async Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _factory.OpenAsync(cancellationToken);

            string EncodedUsr = _hasher.Encode(username);
            // We select the legacy `user_pass` and the new `user_pass_hash`.
            // If the hash column doesn't exist yet (pre-migration schema), the
            // AS clause simply returns NULL for that field.
            const string sql = @"
                                SELECT
                                    usr_id             AS Id,
                                    profile_name       AS UserName,
                                    def_brnch_id       AS DefaultBranchId,
                                    TRUE               AS IsActive,
                                    passwd             AS LegacyPassword,
                                    passwd             AS PasswordHash
                                FROM ctge1075
                                WHERE usr_nam = @EncodedUsr
                                LIMIT 1";
            var cmd = new CommandDefinition(sql, new { EncodedUsr }, cancellationToken: cancellationToken);
            return await conn.QuerySingleOrDefaultAsync<User>(cmd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FindByUsernameAsync failed for username {Username}", username);
            throw;
        }
    }

    public async Task UpdatePasswordHashAsync(int userId, string hash, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _factory.OpenAsync(cancellationToken);
            const string sql = "UPDATE user_master SET user_pass_hash = @hash WHERE user_id = @id";
            var cmd = new CommandDefinition(sql, new { hash, id = userId }, cancellationToken: cancellationToken);
            await conn.ExecuteAsync(cmd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdatePasswordHashAsync failed for userId {UserId}", userId);
            throw;
        }
    }
}
