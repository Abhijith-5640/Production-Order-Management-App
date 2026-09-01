using Microsoft.Extensions.Logging;
using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Orders;

public sealed record GetTariffViolationsQuery(int BrnchId);

public sealed class GetTariffViolationsHandler
    : IHandler<GetTariffViolationsQuery, TariffViolationResponse>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GetTariffViolationsHandler> _logger;

    public GetTariffViolationsHandler(
        IOrderRepository orders,
        ILogger<GetTariffViolationsHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<TariffViolationResponse>> HandleAsync(
        GetTariffViolationsQuery request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _orders.GetTariffViolationsAsync(
                request.BrnchId, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTariffViolations failed");
            return Error.DatabaseError(ex.Message);
        }
    }
}
