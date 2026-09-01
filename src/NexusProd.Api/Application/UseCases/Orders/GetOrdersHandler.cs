using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record GetOrdersQuery(int SectionId, int TripId);
public sealed record GetOrdersResult(IReadOnlyList<OrderItem> Orders);

public sealed class GetOrdersHandler : IHandler<GetOrdersQuery, GetOrdersResult>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GetOrdersHandler> _logger;

    public GetOrdersHandler(IOrderRepository orders, ILogger<GetOrdersHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<GetOrdersResult>> HandleAsync(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        if (request.SectionId == 0 || request.TripId <= 0)
            return Error.InvalidInput("section and trip are required");

        try
        {
            var orders = await _orders.GetOrdersAsync(request.SectionId, request.TripId, cancellationToken);
            return new GetOrdersResult(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetOrders failed for sectionId {SectionId} trip {Trip}", request.SectionId, request.TripId);
            return Error.DatabaseError(ex.Message);
        }
    }
}
