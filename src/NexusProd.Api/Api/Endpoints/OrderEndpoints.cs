using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Api.Mappers;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Application.UseCases.Orders;

namespace NexusProd.Api.Api.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/orders").WithTags("Orders").RequireAuthorization("AuthenticatedUser");

        group.MapGet("/check-pending", async (IHandler<CheckPendingQuery, CheckPendingResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new CheckPendingQuery(), ct);
            return r.ToHttp(p => Results.Ok(new CheckPendingResponse(p.PendingExist)));
        });

        group.MapPost("/generate", async (GenerateInvoicesRequest req, IHandler<GenerateInvoicesCommand, GenerateInvoicesResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GenerateInvoicesCommand(req.UserId), ct);
            return r.ToHttp(g => Results.Ok(new GenerateInvoicesResponse(true, g.Message, g.InvoiceCount)));
        });

        group.MapGet("/", async (int section, int trip, IHandler<GetOrdersQuery, GetOrdersResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetOrdersQuery(section, trip), ct);
            return r.ToHttp(o => Results.Ok(new OrdersResponse(o.Orders.Select(OrderMapper.ToDto).ToList())));
        });

        group.MapPost("/update", async (UpdateOrderRequest req, IHandler<UpdateInvoiceCommand, string> h, CancellationToken ct) =>
        {
            var dist = req.NewDistribution
                .Select(d => new Domain.Entities.DistributionEntry { Branch = d.Branch, Trip = req.Trip, Qty = d.Qty })
                .ToList();
            var r = await h.HandleAsync(new UpdateInvoiceCommand(req.ItemId, req.Trip, dist), ct);
            return r.ToHttp(msg => Results.Ok(new UpdateOrderResponse(true, msg)));
        });

        group.MapPost("/exclude", async (ExcludeRequest req, IHandler<ExcludeItemCommand, string> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new ExcludeItemCommand(req.SectionId, req.ItemId, req.CurrentTrip, req.Branch), ct);
            return r.ToHttp(msg => Results.Ok(new ExcludeResponse(true, msg)));
        });
    }
}
