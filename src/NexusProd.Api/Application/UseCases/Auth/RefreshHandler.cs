using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.UseCases.Auth;

public sealed record RefreshCommand(string RefreshToken);
public sealed record RefreshResult(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);

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

        // Look up the user so the rotated access token carries correct
        // claims (UserName, def_branch, def_counter). Without this the
        // access token would have placeholder zeros and downstream
        // endpoints using those claims would misbehave.
        User user;
        try
        {
            var lookedUp = await _users.FindByIdAsync(userId.Value, cancellationToken);
            if (lookedUp is null)
            {
                _logger.LogWarning("Refresh token references unknown userId {UserId}", userId.Value);
                return Error.Unauthorized("User no longer exists");
            }
            user = lookedUp;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed during user lookup for userId {UserId}", userId.Value);
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

        var (newAccess, _, newAccessExp) = _jwt.IssueAccessToken(user.Id, user.UserName, user.UserBrnchId, user.UserCounterId);
        var (newRefreshToken, newRefreshJti, newRefreshExp) = _jwt.IssueRefreshToken(userId.Value);
        try
        {
            await _refreshTokens.StoreAsync(newRefreshJti, userId.Value, newRefreshExp, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed during new-token store for userId {UserId}", userId.Value);
            return Error.DatabaseError(ex.Message);
        }

        return new RefreshResult(newAccess, newAccessExp, newRefreshToken, newRefreshExp);
    }
}
