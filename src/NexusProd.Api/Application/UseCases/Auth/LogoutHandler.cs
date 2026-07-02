using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Auth;

public sealed record LogoutCommand(string? RefreshJti, string? AccessJti);

/// <summary>
/// Revokes both the access JTI and the refresh JTI so the token pair can
/// no longer be used. Idempotent — calling logout twice is fine.
/// </summary>
public sealed class LogoutHandler : IHandler<LogoutCommand, bool>
{
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IAccessTokenBlacklist _accessBlacklist;
    private readonly ILogger<LogoutHandler> _logger;

    public LogoutHandler(
        IRefreshTokenStore refreshTokens,
        IAccessTokenBlacklist accessBlacklist,
        ILogger<LogoutHandler> logger)
    {
        _refreshTokens = refreshTokens;
        _accessBlacklist = accessBlacklist;
        _logger = logger;
    }

    public async Task<Result<bool>> HandleAsync(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(request.RefreshJti))
        {
            try
            {
                await _refreshTokens.RevokeAsync(request.RefreshJti, isSuperseded: false, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Logout failed during refresh-token revoke for jti {Jti}", request.RefreshJti);
                return Error.DatabaseError(ex.Message);
            }
        }
        if (!string.IsNullOrEmpty(request.AccessJti))
            _accessBlacklist.Revoke(request.AccessJti);
        return true;
    }
}

/// <summary>
/// Side-channel blacklist for access JTIs (logout before expiry).
/// Lives in the security namespace; the abstraction is declared here
/// next to its only consumer.
/// </summary>
public interface IAccessTokenBlacklist
{
    bool IsRevoked(string jti);
    void Revoke(string jti);
}
