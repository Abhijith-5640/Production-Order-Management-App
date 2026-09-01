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
    IReadOnlyList<Domain.Entities.DistributionEntry> Entries,
    int UsrId);

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
        if (request.SectionId == 0 || request.ItemId <= 0 || request.CurrentTrip <= 0 || request.StockMastId <= 0 || request.UsrId <= 0)
            return Error.InvalidInput("section, itemId, currentTrip, stockMastId, and usrId are required for exclusion");

        if (request.Entries is null || request.Entries.Count == 0)
            return Error.InvalidInput("At least one exclude entry is required for exclusion");

        try
        {
            var message = await _orders.ExcludeItemAsync(
                request.SectionId,
                request.ItemId,
                request.StockMastId,
                request.CurrentTrip,
                request.BrnchId,
                request.Entries,
                request.UsrId,
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
