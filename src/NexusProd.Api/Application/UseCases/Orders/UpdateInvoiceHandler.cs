using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record UpdateInvoiceCommand(int ItemId, int Trip, IReadOnlyList<DistributionEntry> NewDistribution);

public sealed class UpdateInvoiceHandler : IHandler<UpdateInvoiceCommand, string>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<UpdateInvoiceHandler> _logger;

    public UpdateInvoiceHandler(IOrderRepository orders, ILogger<UpdateInvoiceHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<string>> HandleAsync(UpdateInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (request.ItemId <= 0) return Error.InvalidInput("itemId is required");
        if (request.Trip <= 0) return Error.InvalidInput("trip is required");
        if (request.NewDistribution is null) return Error.InvalidInput("newDistribution is required");
        if (request.NewDistribution.Count == 0) return Error.InvalidInput("newDistribution is empty");
        if (request.NewDistribution.Any(d => d.PurSaleId <= 0)) return Error.InvalidInput("All rows must have a valid purSaleId");
        if (request.NewDistribution.Any(d => d.StockMastId <= 0)) return Error.InvalidInput("All rows must have a valid stockMastId");
        if (request.NewDistribution.Any(d => d.OriginalQty < 0)) return Error.InvalidInput("originalQty must be >= 0");
        if (request.NewDistribution.Any(d => d.Qty is not null && d.Qty < 0)) return Error.InvalidInput("qty must be >= 0 or null");

        try
        {
            var repoMessage = await _orders.UpdateInvoiceAsync(request.ItemId, request.Trip, request.NewDistribution, cancellationToken);
            return $"Invoice updated in MySQL for item {request.ItemId} — {repoMessage}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateInvoice failed for itemId {ItemId} trip {Trip}", request.ItemId, request.Trip);
            return Error.DatabaseError("Failed to update database: " + ex.Message);
        }
    }
}
