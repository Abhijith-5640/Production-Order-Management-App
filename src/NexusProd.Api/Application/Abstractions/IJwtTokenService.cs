namespace NexusProd.Api.Application.Abstractions;

/// <summary>
/// JWT token issuance abstraction. Production impl is <c>JwtTokenService</c>.
/// </summary>
public interface IJwtTokenService
{
    /// <summary>Builds a short-lived access token (default 15 min).</summary>
    (string Token, string Jti, DateTimeOffset ExpiresAt) IssueAccessToken(int userId, string userName, int defaultBranchId, int defCounterId);

    /// <summary>Builds a long-lived refresh token (default 7 days).</summary>
    (string Token, string Jti, DateTimeOffset ExpiresAt) IssueRefreshToken(int userId);

    /// <summary>Returns the principal inside <paramref name="token"/>, or null if invalid.</summary>
    TokenPrincipal? ValidateRefreshToken(string token);
}

public sealed record TokenPrincipal(int UserId, string Jti, DateTimeOffset ExpiresAt);
