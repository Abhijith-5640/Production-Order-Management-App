using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Application.UseCases.Lookups;

namespace NexusProd.Api.Api.Endpoints;

public static class LookupEndpoints
{
    public static void MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        // /api/sections and /api/trips are anonymous — the original
        // Express app gates them only by IP, not by JWT, so the Login
        // page can populate them before the user logs in.
        app.MapGet("/api/sections", async (IHandler<GetSectionsQuery, GetSectionsResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetSectionsQuery(), ct);
            return r.ToHttp(o => Results.Ok(new SectionsResponse(o.Sections)));
        }).AllowAnonymous();

        app.MapGet("/api/trips", async (int section, IHandler<GetTripsQuery, GetTripsResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetTripsQuery(section), ct);
            return r.ToHttp(o => Results.Ok(new TripsResponse(o.Trips)));
        }).AllowAnonymous();

        app.MapGet("/api/server-info", async (IHandler<GetServerInfoQuery, ServerInfoView> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetServerInfoQuery(), ct);
            return r.ToHttp(v => Results.Ok(new ServerInfoResponse(
                Version: v.Version,
                ServerTime: v.ServerTime,
                UptimeSeconds: v.Uptime.TotalSeconds,
                LanAddresses: v.LanAddresses,
                Port: v.Port)));
        }).AllowAnonymous();

        app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", "1.0.0", DateTimeOffset.UtcNow, 0))).AllowAnonymous();
    }
}
