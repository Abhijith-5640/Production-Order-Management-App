using Microsoft.Extensions.Logging;
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
    private readonly ILogger<RefreshHandler> _logger;

    public RefreshHandler(
        IJwtTokenService jwt,
        IRefreshTokenStore refreshTokens,
        IUserRepository users,
        IClock clock,
        ILogger<RefreshHandler> logger)
    {
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _users = users;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<RefreshResult>> HandleAsync(RefreshCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Error.Unauthorized("Missing refresh token");

        var principal = _jwt.ValidateRefreshToken(request.RefreshToken);
        if (principal is null) return Error.Unauthorized("Invalid refresh token");

        int? userId;
        try
        {
            userId = await _refreshTokens.GetUserIdAsync(principal.Jti, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed during refresh token lookup for jti {Jti}", principal.Jti);
            return Error.DatabaseError(ex.Message);
        }
        if (userId is null) return Error.Unauthorized("Refresh token revoked or unknown");

        // We could also look up the user to re-encode the claims, but
        // for the POC the access token only needs userId. If the user
        // was deleted, this still issues a token that will 404 on the
        // next call — accept that for now.
        try
        {
            _ = await _users.FindByUsernameAsync(string.Empty, cancellationToken); // placeholder no-op
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed during placeholder user lookup for jti {Jti}", principal.Jti);
            return Error.DatabaseError(ex.Message);
        }

        // rotate: revoke old, issue new
        try
        {
            await _refreshTokens.RevokeAsync(principal.Jti, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed during old-token revoke for jti {Jti}", principal.Jti);
            return Error.DatabaseError(ex.Message);
        }

        var (newAccess, _, newAccessExp) = _jwt.IssueAccessToken(userId.Value, string.Empty, 0, 0); // placeholder no-op
        var (_, newRefreshJti, newRefreshExp) = _jwt.IssueRefreshToken(userId.Value);
        try
        {
            await _refreshTokens.StoreAsync(newRefreshJti, userId.Value, newRefreshExp, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed during new-token store for userId {UserId}", userId.Value);
            return Error.DatabaseError(ex.Message);
        }

        return new RefreshResult(newAccess, newAccessExp);
    }
}
