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

    /// <summary>
    /// Marks <paramref name="jti"/> as revoked or superseded.
    /// Pass <paramref name="isSuperseded"/> true for token rotation (grace period applies),
    /// false for hard revocation (logout, no grace period).
    /// Idempotent.
    /// </summary>
    Task RevokeAsync(string jti, bool isSuperseded, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the current status of <paramref name="jti"/>, or null if the JTI is unknown/expired.
    /// Used to distinguish between an intentionally superseded token (recoverable) and
    /// a genuinely invalid/expired/revoked token.
    /// </summary>
    Task<RefreshTokenStatus?> GetTokenStatusAsync(string jti, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true if <paramref name="jti"/> was superseded by a concurrent refresh rotation
    /// within the specified grace period. A hard-revoked JTI always returns false.
    /// </summary>
    Task<bool> WasSupersededWithinGraceAsync(string jti, TimeSpan gracePeriod, CancellationToken cancellationToken);

    /// <summary>
    /// Removes expired and long-superseded entries to prevent unbounded growth.
    /// </summary>
    Task CleanupExpiredAsync(CancellationToken cancellationToken);
}

/// <summary>The lifecycle status of a stored refresh-token JTI.</summary>
public enum RefreshTokenStatus
{
    /// <summary>Valid and usable for refresh.</summary>
    Active = 0,
    /// <summary>Superseded by a newer token during a rotation; still queryable during the grace window.</summary>
    Superseded = 1,
    /// <summary>Hard-revoked (logout); not recoverable.</summary>
    HardRevoked = 2,
}
