using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Application.UseCases.Config;

namespace NexusProd.Api.Api.Endpoints;

public static class ConfigEndpoints
{
    public static void MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/config").WithTags("Config").AllowAnonymous();

        group.MapGet("/status", async (IHandler<GetConfigStatusQuery, GetConfigStatusResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetConfigStatusQuery(), ct);
            return r.ToHttp(v => Results.Ok(new ConfigStatusResponse(
                v.Configured, v.Host, v.Port, v.Database, v.User, v.Password)));
        });

        group.MapPost("/save", async (DbConfigRequest req, IHandler<SaveConfigCommand, string> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new SaveConfigCommand(req.Host, req.Port, req.User, req.Password, req.Database, req.UseMockDb), ct);
            return r.ToHttp(msg => Results.Ok(new SuccessResponse(true, msg)));
        });

        group.MapPost("/test", async (TestDbRequest req, IHandler<TestDbCommand, string> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new TestDbCommand(req.Host, req.Port, req.User, req.Password, req.Database), ct);
            if (r.IsFailure) return Results.Json(new TestDbResponse(false, r.Error.Message), statusCode: StatusCodes.Status500InternalServerError);
            return Results.Ok(new TestDbResponse(true, r.Value!));
        });
    }
}
