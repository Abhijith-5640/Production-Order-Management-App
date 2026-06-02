using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record CheckPendingQuery();
public sealed record CheckPendingResult(bool PendingExist);

public sealed class CheckPendingHandler : IHandler<CheckPendingQuery, CheckPendingResult>
{
    private readonly IOrderRepository _orders;

    public CheckPendingHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<CheckPendingResult>> HandleAsync(CheckPendingQuery request, CancellationToken cancellationToken)
    {
        var pending = await _orders.CheckPendingOrdersAsync(cancellationToken);
        return new CheckPendingResult(pending);
    }
}
