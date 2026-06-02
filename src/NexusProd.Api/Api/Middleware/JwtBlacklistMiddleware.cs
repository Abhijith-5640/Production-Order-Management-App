using System.IdentityModel.Tokens.Jwt;
using NexusProd.Api.Application.UseCases.Auth;

namespace NexusProd.Api.Api.Middleware;

/// <summary>
/// After JwtBearer validates the token, the middleware checks the JTI
/// against the in-memory blacklist. If the access token was explicitly
/// logged out, the request is rejected with 401.
/// </summary>
public sealed class JwtBlacklistMiddleware
{
    private readonly RequestDelegate _next;

    public JwtBlacklistMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAccessTokenBlacklist blacklist)
    {
        // Skip for anonymous endpoints — JwtBearer won't populate the user
        // for them anyway, so this is cheap.
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var jti = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (!string.IsNullOrEmpty(jti) && blacklist.IsRevoked(jti))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { success = false, message = "Token revoked" });
                return;
            }
        }
        await _next(context);
    }
}
