namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// In-memory store for refresh-token JTIs. Single-instance POC scope;
/// swap with a Redis-backed implementation when scaling out.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Remembers the JTI as valid for the given user.</summary>
    Task StoreAsync(string jti, int userId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Returns the user bound to <paramref name="jti"/>, or null if unknown / revoked / expired.</summary>
    Task<int?> GetUserIdAsync(string jti, CancellationToken cancellationToken);

    /// <summary>Marks <paramref name="jti"/> as revoked. Idempotent.</summary>
    Task RevokeAsync(string jti, CancellationToken cancellationToken);
}
