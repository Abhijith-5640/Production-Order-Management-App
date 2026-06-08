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
        if (request.ItemId <= 0 || request.Trip <= 0 || request.NewDistribution is null)
            return Error.InvalidInput("itemId, trip, and newDistribution are required");

        try
        {
            await _orders.UpdateInvoiceAsync(request.ItemId, request.Trip, request.NewDistribution, cancellationToken);
            return $"Invoice updated in MySQL for item {request.ItemId}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateInvoice failed for itemId {ItemId} trip {Trip}", request.ItemId, request.Trip);
            return Error.DatabaseError("Failed to update database: " + ex.Message);
        }
    }
}
