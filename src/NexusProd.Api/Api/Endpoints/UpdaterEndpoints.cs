using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Application.UseCases.Config;

namespace NexusProd.Api.Api.Endpoints;

public static class UpdaterEndpoints
{
    public static void MapUpdaterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/updater").WithTags("Updater").AllowAnonymous();

        group.MapGet("/status", async (IHandler<GetUpdateStatusQuery, GetUpdateStatusResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetUpdateStatusQuery(), ct);
            return r.ToHttp(s => Results.Ok(new UpdateStatusResponse(
                Phase: s.Status.Phase.ToString(),
                Message: s.Status.Message,
                LatestVersion: s.Status.LatestVersion,
                LastChecked: s.Status.LastChecked)));
        });

        group.MapPost("/check", async (IHandler<CheckUpdateCommand, CheckUpdateResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new CheckUpdateCommand(), ct);
            return r.ToHttp(c => Results.Ok(new CheckUpdateResponse(c.Accepted, c.Message)));
        });
    }
}
