using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Domain.Entities;
using NexusProd.Api.Infrastructure.Configuration;

namespace NexusProd.Api.Application.UseCases.Auth;

public sealed record RefreshCommand(string RefreshToken);
public sealed record RefreshResult(string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);

/// <summary>
/// Validates a presented refresh token, marks the old JTI superseded (not hard-revoked),
/// and issues a fresh access token. The refresh token itself is rotated.
///
/// Concurrency handling:
/// - A JTI that was valid but superseded by a concurrent refresh within the grace window
///   is reported as <see cref="Error.TokenAlreadyRotated"/> so the caller can retry once.
/// - A genuinely invalid/unknown/expired/hard-revoked JTI returns <see cref="Error.Unauthorized"/>
///   — the client must re-login.
/// </summary>
public sealed class RefreshHandler : IHandler<RefreshCommand, RefreshResult>
{
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IUserRepository _users;
    private readonly IClock _clock;
    private readonly ILogger<RefreshHandler> _logger;
    private readonly int _graceSeconds;

    public RefreshHandler(
        IJwtTokenService jwt,
        IRefreshTokenStore refreshTokens,
        IUserRepository users,
        IClock clock,
        ILogger<RefreshHandler> logger,
        IOptions<JwtSettings> jwtOptions)
    {
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _users = users;
        _clock = clock;
        _logger = logger;
        _graceSeconds = jwtOptions.Value.RefreshTokenRotationGraceSeconds;
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

        if (userId is null)
        {
            // Not found in the active store. Could be genuinely invalid/expired/revoked,
            // or it could have been superseded by a concurrent refresh within the grace window.
            var wasSuperseded = false;
            try
            {
                wasSuperseded = await _refreshTokens.WasSupersededWithinGraceAsync(
                    principal.Jti, TimeSpan.FromSeconds(_graceSeconds), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not check superseded-grace for jti {Jti}; treating as hard failure", principal.Jti);
            }

            if (wasSuperseded)
                return Error.TokenAlreadyRotated("token_already_rotated");

            return Error.Unauthorized("Refresh token revoked or unknown");
        }

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

        // rotate: supersede old JTI (grace period applies), issue new
        try
        {
            await _refreshTokens.RevokeAsync(principal.Jti, isSuperseded: true, cancellationToken);
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
