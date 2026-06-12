using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record ExcludeItemCommand(
    int SectionId,
    int ItemId,
    int CurrentTrip,
    int StockMastId,
    int? BrnchId,
    IReadOnlyList<int> PurSaleIds);

public sealed class ExcludeItemHandler : IHandler<ExcludeItemCommand, string>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<ExcludeItemHandler> _logger;

    public ExcludeItemHandler(IOrderRepository orders, ILogger<ExcludeItemHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<string>> HandleAsync(ExcludeItemCommand request, CancellationToken cancellationToken)
    {
        if (request.SectionId <= 0 || request.ItemId <= 0 || request.CurrentTrip <= 0 || request.StockMastId <= 0)
            return Error.InvalidInput("section, itemId, currentTrip, and stockMastId are required for exclusion");

        if (request.PurSaleIds is null || request.PurSaleIds.Count == 0)
            return Error.InvalidInput("At least one purSaleId is required for exclusion");

        // Single-item guard: for each purSaleId, verify the bill has at least
        // one other stock_mast_id (i.e. multiple items). If any bill carries
        // only this stockMastId, block the exclusion.
        var singleItemBill = await _orders.FindSingleItemBillAsync(
            request.PurSaleIds, request.StockMastId, cancellationToken);

        if (singleItemBill is not null)
            return Error.InvalidInput($"Bill {singleItemBill.Value.PurSaleId} contains only this item and cannot be excluded.");

        try
        {
            var message = await _orders.ExcludeItemAsync(
                request.SectionId,
                request.ItemId,
                request.StockMastId,
                request.CurrentTrip,
                request.BrnchId,
                request.PurSaleIds,
                cancellationToken);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExcludeItem failed for sectionId {SectionId} itemId {ItemId} trip {Trip} brnchId {BrnchId}", request.SectionId, request.ItemId, request.CurrentTrip, request.BrnchId);
            return Error.DatabaseError("Failed to exclude item: " + ex.Message);
        }
    }
}
