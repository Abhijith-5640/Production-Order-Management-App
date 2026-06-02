using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record ExcludeItemCommand(string Section, int ItemId, string CurrentTrip, string? Branch);

public sealed class ExcludeItemHandler : IHandler<ExcludeItemCommand, string>
{
    private readonly IOrderRepository _orders;

    public ExcludeItemHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<string>> HandleAsync(ExcludeItemCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Section) || request.ItemId <= 0 || string.IsNullOrWhiteSpace(request.CurrentTrip))
            return Error.InvalidInput("section, itemId, and currentTrip are required for exclusion");

        try
        {
            var message = await _orders.ExcludeItemAsync(request.Section, request.ItemId, request.CurrentTrip, request.Branch, cancellationToken);
            return message;
        }
        catch (Exception ex)
        {
            return Error.DatabaseError("Failed to exclude item: " + ex.Message);
        }
    }
}
