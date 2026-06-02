using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record UpdateInvoiceCommand(int ItemId, string Trip, IReadOnlyList<DistributionEntry> NewDistribution);

public sealed class UpdateInvoiceHandler : IHandler<UpdateInvoiceCommand, string>
{
    private readonly IOrderRepository _orders;

    public UpdateInvoiceHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<string>> HandleAsync(UpdateInvoiceCommand request, CancellationToken cancellationToken)
    {
        if (request.ItemId <= 0 || string.IsNullOrWhiteSpace(request.Trip) || request.NewDistribution is null)
            return Error.InvalidInput("itemId, trip, and newDistribution are required");

        try
        {
            await _orders.UpdateInvoiceAsync(request.ItemId, request.Trip, request.NewDistribution, cancellationToken);
            return $"Invoice updated in MySQL for item {request.ItemId}";
        }
        catch (Exception ex)
        {
            return Error.DatabaseError("Failed to update database: " + ex.Message);
        }
    }
}
