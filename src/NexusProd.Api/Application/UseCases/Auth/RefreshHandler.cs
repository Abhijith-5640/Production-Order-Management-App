using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Auth;

public sealed record RefreshCommand(string RefreshToken);
public sealed record RefreshResult(string AccessToken, DateTimeOffset AccessExpiresAt);

/// <summary>
/// Validates a presented refresh token, marks the old JTI revoked, and
/// issues a fresh access token. The refresh token itself is rotated.
/// </summary>
public sealed class RefreshHandler : IHandler<RefreshCommand, RefreshResult>
{
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUserRepository _users;
    private readonly IClock _clock;

    public RefreshHandler(
        IJwtTokenService jwt,
        IRefreshTokenStore refreshTokens,
        IUserRepository users,
        IClock clock)
    {
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<RefreshResult>> HandleAsync(RefreshCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Error.Unauthorized("Missing refresh token");

        var principal = _jwt.ValidateRefreshToken(request.RefreshToken);
        if (principal is null) return Error.Unauthorized("Invalid refresh token");

        var userId = await _refreshTokens.GetUserIdAsync(principal.Jti, cancellationToken);
        if (userId is null) return Error.Unauthorized("Refresh token revoked or unknown");

        // We could also look up the user to re-encode the claims, but
        // for the POC the access token only needs userId. If the user
        // was deleted, this still issues a token that will 404 on the
        // next call — accept that for now.
        _ = await _users.FindByUsernameAsync(string.Empty, cancellationToken); // placeholder no-op

        // rotate: revoke old, issue new
        await _refreshTokens.RevokeAsync(principal.Jti, cancellationToken);
        var (newAccess, _, newAccessExp) = _jwt.IssueAccessToken(userId.Value, string.Empty, 0);
        var (_, newRefreshJti, newRefreshExp) = _jwt.IssueRefreshToken(userId.Value);
        await _refreshTokens.StoreAsync(newRefreshJti, userId.Value, newRefreshExp, cancellationToken);

        return new RefreshResult(newAccess, newAccessExp);
    }
}
