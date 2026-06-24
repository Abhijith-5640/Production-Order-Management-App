using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// Repository abstraction for users. The Dapper implementation lives in
/// <c>Infrastructure/Persistence</c> and reads from the <c>user_master</c>
/// table (see <c>MySQL_Assets/prod_app_db_meta_data.sql</c>).
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Returns the user with the matching <paramref name="username"/>, or
    /// <c>null</c> if no such user exists. Includes the legacy plain-text
    /// password and the bcrypt hash (if any) so the application layer can
    /// decide how to verify credentials and migrate the hash on success.
    /// </summary>
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the user with the matching <paramref name="userId"/>, or
    /// <c>null</c> if no such user exists. Used by the refresh flow to
    /// re-issue an access token whose claims (branch, counter, name)
    /// match the current user record.
    /// </summary>
    Task<User?> FindByIdAsync(int userId, CancellationToken cancellationToken);
}
