using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Application.UseCases.Auth;
using NexusProd.Api.Infrastructure.Configuration;

namespace NexusProd.Api.Api.Endpoints;

/// <summary>
/// Login / refresh / logout / me.
/// - Login and Refresh set the httpOnly refresh-token cookie.
/// - Access tokens travel in the JSON body and are held in JS memory.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (
            LoginRequest req,
            IHandler<LoginCommand, LoginResult> handler,
            IOptions<JwtSettings> jwtOptions,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            var command = new LoginCommand(req.Username, req.Password);
            var result = await handler.HandleAsync(command, ct);
            return await result.ToHttpAsync(async login =>
            {
                // The login handler issues both an access and a refresh
                // token. The refresh token is delivered to the browser
                // via an HttpOnly cookie so JS cannot exfiltrate it; the
                // access token travels in the JSON body. The JTI of the
                // refresh token is already stored server-side by the
                // handler, so a logout or a /refresh can revoke/rotate it.
                ctx.Response.Cookies.Append(jwtOptions.Value.CookieName, login.RefreshToken, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = ctx.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    Expires = login.RefreshExpiresAt
                });
                await Task.CompletedTask;
                return Results.Ok(new LoginResponse(login.AccessToken, login.AccessExpiresAt, login.User, login.UserId, login.UserBrnchId, login.UserCounterId));
            });
        }).AllowAnonymous();

        group.MapPost("/refresh", async (
            HttpContext ctx,
            IOptions<JwtSettings> jwtOptions,
            IHandler<RefreshCommand, RefreshResult> handler,
            CancellationToken ct) =>
        {
            if (!ctx.Request.Cookies.TryGetValue(jwtOptions.Value.CookieName, out var cookieValue) || string.IsNullOrEmpty(cookieValue))
                return Results.Json(new { success = false, message = "Missing refresh cookie" }, statusCode: StatusCodes.Status401Unauthorized);

            // The cookie now carries the real refresh JWT issued at login.
            // The handler validates it, rotates the JTI server-side, and
            // returns a fresh access token. Note: the rotated refresh
            // token is NOT written back to the cookie here — clients that
            // need a rotated cookie should log in again. For the silent-
            // refresh use case (re-issuing an access token) the access
            // token alone is enough.
            var command = new RefreshCommand(cookieValue);
            var result = await handler.HandleAsync(command, ct);
            return result.ToHttp(refresh => Results.Ok(new RefreshResponse(refresh.AccessToken, refresh.AccessExpiresAt)));
        }).AllowAnonymous();

        group.MapPost("/logout", async (
            HttpContext ctx,
            IOptions<JwtSettings> jwtOptions,
            IJwtTokenService jwt,
            IHandler<LogoutCommand, bool> handler,
            CancellationToken ct) =>
        {
            var accessJti = ctx.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;

            // Server-side refresh revocation: validate the cookie's
            // refresh JWT and pull out its JTI. The handler will mark it
            // revoked in the in-memory store, so a stolen-then-logged-out
            // cookie cannot be silently refreshed.
            string? refreshJti = null;
            if (ctx.Request.Cookies.TryGetValue(jwtOptions.Value.CookieName, out var refreshCookie)
                && !string.IsNullOrEmpty(refreshCookie))
            {
                refreshJti = jwt.ValidateRefreshToken(refreshCookie)?.Jti;
            }

            var command = new LogoutCommand(RefreshJti: refreshJti, AccessJti: accessJti);
            var result = await handler.HandleAsync(command, ct);

            ctx.Response.Cookies.Delete(jwtOptions.Value.CookieName, new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/"
            });

            return result.ToHttp(_ => Results.Ok(new LogoutResponse(true)));
        }).RequireAuthorization("AuthenticatedUser");

        group.MapGet("/me", (HttpContext ctx) =>
        {
            var sub = ctx.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var name = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
            _ = int.TryParse(sub, out var userId);
            return Results.Ok(new MeResponse(userId, name ?? string.Empty));
        }).RequireAuthorization("AuthenticatedUser");
    }
}
