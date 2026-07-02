using System.IdentityModel.Tokens.Jwt;
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

        group.MapGet("/check-pending", async (int? brnchId, IHandler<CheckPendingQuery, CheckPendingResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new CheckPendingQuery(brnchId ?? 0), ct);
            return r.ToHttp(p => Results.Ok(new CheckPendingResponse(p.PendingExist)));
        });

        group.MapPost("/generate", async (GenerateInvoicesRequest req, IHandler<GenerateInvoicesCommand, GenerateInvoicesResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GenerateInvoicesCommand(req.UserId, req.BrnchId, req.UserCounterId), ct);
            return r.ToHttp(g => Results.Ok(new GenerateInvoicesResponse(true, g.Message, g.InvoiceCount)));
        });

        group.MapGet("/", async (int section, int trip, IHandler<GetOrdersQuery, GetOrdersResult> h, CancellationToken ct) =>
        {
            var r = await h.HandleAsync(new GetOrdersQuery(section, trip), ct);
            return r.ToHttp(o => Results.Ok(new OrdersResponse(o.Orders.Select(OrderMapper.ToDto).ToList())));
        });

        group.MapPost("/update", async (UpdateOrderRequest req, IHandler<UpdateInvoiceCommand, string> h, HttpContext ctx, CancellationToken ct) =>
        {
            var usrId = req.UsrId;
            var dist = req.Distribution
                .Select(d => new Domain.Entities.DistributionEntry
                {
                    PurSaleId = d.PurSaleId,
                    StockMastId = d.StockMastId,
                    OriginalQty = d.OriginalQty,
                    Branch = d.Branch,
                    Trip = req.Trip,
                    Qty = d.Qty,
                    TargetTrip = d.TargetTrip,
                })
                .ToList();
            var r = await h.HandleAsync(new UpdateInvoiceCommand(req.ItemId, req.Trip, dist, usrId), ct);
            return r.ToHttp(msg => Results.Ok(new UpdateOrderResponse(true, msg)));
        });

        group.MapPost("/exclude", async (ExcludeRequest req, IHandler<ExcludeItemCommand, string> h, HttpContext ctx, CancellationToken ct) =>
        {
            var usrId = req.UsrId;
            // Map each per-row ExcludeEntry into the domain DistributionEntry. The
            // server-side ExcludeItemAsync mirrors UpdateInvoiceAsync — it uses
            // d.Qty and d.OriginalQty to decide between DELETE and UPDATE on the
            // source row, then routes the diff to d.TargetTrip if provided.
            //
            // OriginalQty is set to e.Qty here: the UI's dirty-qty guard ensures
            // the qty sent from the modal always equals the original qty of the
            // row (any unsaved qty changes are either reset to original or block
            // the exclusion with a confirmation). With OriginalQty == Qty, the
            // server takes the "full row exclude" DELETE path. The OriginalQty
            // field is kept equal to Qty to satisfy the invariant that the
            // "Qty == OriginalQty → skip" filter in UpdateInvoiceAsync sees
            // a clean no-op for this case.
            var dist = (req.Entries ?? Array.Empty<ExcludeEntry>())
                .Select(e => new Domain.Entities.DistributionEntry
                {
                    PurSaleId = e.PurSaleId,
                    StockMastId = req.StockMastId,
                    OriginalQty = e.Qty,
                    Branch = string.Empty,
                    Trip = req.CurrentTrip,
                    Qty = e.Qty,
                    TargetTrip = e.TargetTrip,
                })
                .ToList();
            var r = await h.HandleAsync(new ExcludeItemCommand(
                req.SectionId,
                req.ItemId,
                req.CurrentTrip,
                req.StockMastId,
                req.BrnchId,
                dist,
                usrId), ct);
            return r.ToHttp(msg => Results.Ok(new ExcludeResponse(true, msg)));
        });
    }
}
