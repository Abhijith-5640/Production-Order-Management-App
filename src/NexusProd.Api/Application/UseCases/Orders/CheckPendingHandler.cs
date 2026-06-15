using Microsoft.Extensions.Logging;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record CheckPendingQuery(int UserBrnchId);
public sealed record CheckPendingResult(bool PendingExist);

public sealed class CheckPendingHandler : IHandler<CheckPendingQuery, CheckPendingResult>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<CheckPendingHandler> _logger;

    public CheckPendingHandler(IOrderRepository orders, ILogger<CheckPendingHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<CheckPendingResult>> HandleAsync(CheckPendingQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var pending = await _orders.CheckPendingOrdersAsync(request.UserBrnchId, cancellationToken);
            return new CheckPendingResult(pending);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckPending failed");
            return Error.DatabaseError(ex.Message);
        }
    }
}
