using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record GetOrdersQuery(string Section, string Trip);
public sealed record GetOrdersResult(IReadOnlyList<OrderItem> Orders);

public sealed class GetOrdersHandler : IHandler<GetOrdersQuery, GetOrdersResult>
{
    private readonly IOrderRepository _orders;

    public GetOrdersHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<GetOrdersResult>> HandleAsync(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Section) || string.IsNullOrWhiteSpace(request.Trip))
            return Error.InvalidInput("section and trip are required");

        var orders = await _orders.GetOrdersAsync(request.Section, request.Trip, cancellationToken);
        return new GetOrdersResult(orders);
    }
}
