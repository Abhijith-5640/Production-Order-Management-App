using System.Text;
using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.UseCases.Auth;

public sealed record LoginCommand(string Username, string Password);
public sealed record LoginResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessExpiresAt,
    DateTimeOffset RefreshExpiresAt,
    string User,
    int UserId,
    int UserBrnchId,
    int UserCounterId);

/// <summary>
/// Authenticates a user. Falls back to the legacy plain-text password
/// column on first login, and transparently writes the bcrypt hash so
/// subsequent logins use the secure path.
/// </summary>
public sealed class LoginHandler : IHandler<LoginCommand, LoginResult>
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenStore _refreshTokens;
    private readonly IClock _clock;
    private readonly ILogger<LoginHandler> _logger;

    public LoginHandler(
        IUserRepository users,
        IPasswordHasher hasher,
        IJwtTokenService jwt,
        IRefreshTokenStore refreshTokens,
        IClock clock,
        ILogger<LoginHandler> logger)
    {
        _users = users;
        _hasher = hasher;
        _jwt = jwt;
        _refreshTokens = refreshTokens;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result<LoginResult>> HandleAsync(LoginCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Error.InvalidInput("Username and password are required.");

        User? user;
        try
        {
            user = await _users.FindByUsernameAsync(request.Username, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed during user lookup for username {Username}", request.Username);
            return Error.DatabaseError(ex.Message);
        }
        if (user is null || !user.IsActive)
            return Error.Unauthorized("Invalid credentials");

        var ok = false;
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            ok = _hasher.Verify(request.Password, user.PasswordHash);
        }

        if (!ok) return Error.Unauthorized("Invalid credentials");

        var (accessToken, _, accessExpires) = _jwt.IssueAccessToken(user.Id, user.UserName, user.UserBrnchId, user.UserCounterId);
        var (refreshToken, refreshJti, refreshExpires) = _jwt.IssueRefreshToken(user.Id);

        try
        {
            await _refreshTokens.StoreAsync(refreshJti, user.Id, refreshExpires, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed during refresh token store for userId {UserId}", user.Id);
            return Error.DatabaseError(ex.Message);
        }

        // Note: refreshToken is returned only so the API layer can set
        // it as an httpOnly cookie. The access token goes in the JSON body.
        return new LoginResult(
            AccessToken: accessToken,
            RefreshToken: refreshToken,
            AccessExpiresAt: accessExpires,
            RefreshExpiresAt: refreshExpires,
            User: user.UserName,
            UserId: user.Id,
            UserBrnchId: user.UserBrnchId,
            UserCounterId: user.UserCounterId);
    }
}
