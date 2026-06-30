using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using NexusProd.Api.Api.Endpoints;
using NexusProd.Api.Api.Filters;
using NexusProd.Api.Api.Middleware;
using NexusProd.Api.Application;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.UseCases.Auth;
using NexusProd.Api.Infrastructure;
using NexusProd.Api.Infrastructure.Configuration;
using NexusProd.Api.Updater;

var builder = WebApplication.CreateBuilder(args);

// ← ADD THIS BLOCK HERE
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddJsonFile("appsettings.local.json", optional: true)
    .AddEnvironmentVariables();

// -----------------------------------------------------------------------------
// Logging — simple console; file logging can be added via Serilog later.
// -----------------------------------------------------------------------------
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(opt =>
{
    opt.SingleLine = true;
    opt.TimestampFormat = "HH:mm:ss ";
});

// -----------------------------------------------------------------------------
// Strongly-typed settings. Reads from appsettings.json + env vars + command line.
// -----------------------------------------------------------------------------
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
builder.Services.Configure<UpdateServerSettings>(builder.Configuration.GetSection("UpdateServerSettings"));

// -----------------------------------------------------------------------------
// Application + Infrastructure + Updater
// -----------------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddUpdater();

// -----------------------------------------------------------------------------
// AuthN / AuthZ — HS256 JWT bearer.
// -----------------------------------------------------------------------------
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();

// Fail fast — secrets must be set in appsettings.local.json or env vars
if (string.IsNullOrWhiteSpace(jwtSettings.AccessSecret) || string.IsNullOrWhiteSpace(jwtSettings.RefreshSecret))
{
    // First boot — generate and persist secrets automatically
    var localPath = Path.Combine(
        builder.Environment.ContentRootPath, "appsettings.local.json");

    jwtSettings.AccessSecret = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    jwtSettings.RefreshSecret = Convert.ToBase64String(
        System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));

    var generated = new
    {
        JwtSettings = new
        {
            jwtSettings.AccessSecret,
            jwtSettings.RefreshSecret
        }
    };

    File.WriteAllText(localPath,
        System.Text.Json.JsonSerializer.Serialize(generated,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

    // Re-apply so the running instance uses the new secrets
    builder.Services.PostConfigure<JwtSettings>(opts =>
    {
        opts.AccessSecret = jwtSettings.AccessSecret;
        opts.RefreshSecret = jwtSettings.RefreshSecret;
    });

    Console.WriteLine("First boot — JWT secrets generated and saved to appsettings.local.json");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.AccessSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role
        };

        options.Events = new JwtBearerEvents
        {
            // Detect token expiration during authentication but DO NOT write
            // the response here. Writing in OnAuthenticationFailed causes a
            // double-fault: the framework's challenge handler also tries to
            // write a 401 after the policy fails, and the second write throws
            // "StatusCode cannot be set because the response has already
            // started." We stash the reason in HttpContext.Items and let
            // OnChallenge emit the body once the framework decides to write
            // the challenge.
            OnAuthenticationFailed = ctx =>
            {
                if (ctx.Exception is SecurityTokenExpiredException)
                {
                    ctx.HttpContext.Items["jwt_error"] = "token_expired";
                }
                return Task.CompletedTask;
            },
            OnChallenge = async ctx =>
            {
                // Only customize the body when the reason is token expiration.
                // For other 401 reasons (missing/invalid token, policy failure
                // for an authenticated user, etc.) let the framework write its
                // default challenge body.
                if (ctx.HttpContext.Items["jwt_error"] is string reason
                    && string.Equals(reason, "token_expired", StringComparison.Ordinal))
                {
                    ctx.HandleResponse(); // suppress default challenge body
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(
                        "{\"error\":\"token_expired\",\"message\":\"Access token expired. Please refresh.\"}");
                }
            }
        };
    });

builder.Services.AddAuthorization(opts =>
{
    opts.AddPolicy("AuthenticatedUser", p => p.RequireAuthenticatedUser());
});

// -----------------------------------------------------------------------------
// MVC / endpoints. We use the minimal-API style and let the global exception
// handler turn 500s into RFC7807 problem details.
// -----------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// -----------------------------------------------------------------------------
// CORS — the Vite dev server hits the API from a different origin in dev.
// In production the SPA is served from the same origin, so the policy is
// locked down to loopback + the LAN.
// -----------------------------------------------------------------------------
builder.Services.AddCors(opts =>
{
    opts.AddDefaultPolicy(p =>
    {
        p.SetIsOriginAllowed(origin =>
               origin.StartsWith("http://localhost") || origin.StartsWith("http://127.0.0.1")
            || origin.StartsWith("https://localhost") || origin.StartsWith("https://127.0.0.1"))
         .AllowAnyHeader()
         .AllowAnyMethod()
         .AllowCredentials();
    });
});

var app = builder.Build();

// -----------------------------------------------------------------------------
// Pipeline order matters. Order:
//   1. global exception handler  (catches everything below)
//   2. HTTPS redirect (only outside dev)
//   3. static files              (so / and /assets/* work without auth)
//   4. CORS                      (must be before auth)
//   5. routing + authN
//   6. JWT blacklist middleware  (only relevant once authN ran)
//   7. endpoints
// -----------------------------------------------------------------------------
app.UseExceptionHandler();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.UseRouting();
app.UseAuthentication();
app.UseMiddleware<JwtBlacklistMiddleware>();
app.UseAuthorization();

// -----------------------------------------------------------------------------
// Map endpoints.
// -----------------------------------------------------------------------------
app.MapAuthEndpoints();
app.MapOrderEndpoints();
app.MapLookupEndpoints();
app.MapConfigEndpoints();
app.MapUpdaterEndpoints();

// -----------------------------------------------------------------------------
// SPA fallback — anything that didn't match a known route returns the React
// index.html so client-side routing works on refresh.
// -----------------------------------------------------------------------------
app.MapFallbackToFile("index.html");

app.Run();
