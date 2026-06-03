using Microsoft.Extensions.Logging;
using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Lookups;

public sealed record GetTripsQuery(int SectionId);
public sealed record GetTripsResult(IReadOnlyList<TripsM> Trips);

public sealed class GetTripsHandler : IHandler<GetTripsQuery, GetTripsResult>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GetTripsHandler> _logger;

    public GetTripsHandler(IOrderRepository orders, ILogger<GetTripsHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<GetTripsResult>> HandleAsync(GetTripsQuery request, CancellationToken cancellationToken)
    {
        if (request.SectionId <= 0)
            return Error.InvalidInput("section is required");

        try
        {
            var trips = await _orders.GetTripsAsync(request.SectionId, cancellationToken);
            return new GetTripsResult(trips);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTrips failed for sectionId {SectionId}", request.SectionId);
            return Error.DatabaseError(ex.Message);
        }
    }
}
