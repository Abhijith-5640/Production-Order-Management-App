using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record ExcludeItemCommand(int SectionId, int ItemId, string CurrentTrip, string? Branch);

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
        if (request.SectionId <= 0 || request.ItemId <= 0 || string.IsNullOrWhiteSpace(request.CurrentTrip))
            return Error.InvalidInput("section, itemId, and currentTrip are required for exclusion");

        try
        {
            var message = await _orders.ExcludeItemAsync(request.SectionId, request.ItemId, request.CurrentTrip, request.Branch, cancellationToken);
            return message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExcludeItem failed for sectionId {SectionId} itemId {ItemId} trip {Trip} branch {Branch}", request.SectionId, request.ItemId, request.CurrentTrip, request.Branch);
            return Error.DatabaseError("Failed to exclude item: " + ex.Message);
        }
    }
}
