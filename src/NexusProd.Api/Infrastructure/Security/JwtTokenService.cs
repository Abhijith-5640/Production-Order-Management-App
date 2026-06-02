using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Infrastructure.Configuration;

namespace NexusProd.Api.Infrastructure.Security;

/// <summary>
/// HS256 JWT issuer. Two independent signing keys (access and refresh)
/// so a refresh token can never be used as an access token even if both
/// are leaked.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _settings;
    private readonly IClock _clock;

    public JwtTokenService(JwtSettings settings, IClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public (string Token, string Jti, DateTimeOffset ExpiresAt) IssueAccessToken(int userId, string userName, int defaultBranchId)
    {
        var jti = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow;
        var exp = now.AddMinutes(_settings.AccessTokenLifetimeMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new Claim(ClaimTypes.Name, userName ?? string.Empty),
            new Claim("def_branch", defaultBranchId.ToString())
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.AccessSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: exp.UtcDateTime,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), jti, exp);
    }

    public (string Token, string Jti, DateTimeOffset ExpiresAt) IssueRefreshToken(int userId)
    {
        var jti = Guid.NewGuid().ToString("N");
        var now = _clock.UtcNow;
        var exp = now.AddDays(_settings.RefreshTokenLifetimeDays);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, jti),
            new Claim("typ", "refresh"),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.RefreshSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: exp.UtcDateTime,
            signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(jwt), jti, exp);
    }

    public TokenPrincipal? ValidateRefreshToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.RefreshSecret));
            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _settings.Issuer,
                ValidateAudience = true,
                ValidAudience = _settings.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            }, out var validated);
            var jwt = (JwtSecurityToken)validated;
            var sub = jwt.Subject;
            var jti = jwt.Id;
            if (!int.TryParse(sub, out var userId)) return null;
            return new TokenPrincipal(userId, jti, jwt.ValidTo);
        }
        catch
        {
            return null;
        }
    }
}
