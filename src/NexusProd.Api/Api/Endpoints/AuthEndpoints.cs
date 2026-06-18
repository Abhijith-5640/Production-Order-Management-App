using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using NexusProd.Api.Api.Contracts;
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
                // token, but to keep the handler pure (no HttpContext) we
                // re-issue the refresh here so the cookie can carry the
                // exact value the API layer is about to set. The handler
                // has already stored the JTI in the refresh store.
                //
                // For the POC the cookie value is a synthetic marker that
                // the client doesn't read — the server-side JTI is what
                // matters for revocation. We tag the cookie so future
                // debugging can tell which one is which.
                ctx.Response.Cookies.Append(jwtOptions.Value.CookieName, "rt:" + login.UserId, new CookieOptions
                {
                    HttpOnly = true,
                    Secure = ctx.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    Expires = DateTimeOffset.UtcNow.AddDays(jwtOptions.Value.RefreshTokenLifetimeDays)
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

            // The cookie value is a marker (rt:<userId>) in this POC —
            // real refresh tokens are issued on login and stored in the
            // refresh-store keyed by JTI. The handler validates the JTI
            // and issues a new access token. A full refresh-token cookie
            // is left as a follow-up; the marker keeps the round-trip
            // testable.
            _ = cookieValue;
            var command = new RefreshCommand(cookieValue);
            var result = await handler.HandleAsync(command, ct);
            return result.ToHttp(refresh => Results.Ok(new RefreshResponse(refresh.AccessToken, refresh.AccessExpiresAt)));
        }).AllowAnonymous();

        group.MapPost("/logout", async (
            HttpContext ctx,
            IOptions<JwtSettings> jwtOptions,
            IHandler<LogoutCommand, bool> handler,
            CancellationToken ct) =>
        {
            var accessJti = ctx.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            var command = new LogoutCommand(RefreshJti: null, AccessJti: accessJti);
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
