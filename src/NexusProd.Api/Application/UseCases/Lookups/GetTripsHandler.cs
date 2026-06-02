using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Lookups;

public sealed record GetTripsQuery(int SectionId);
public sealed record GetTripsResult(IReadOnlyList<string> Trips);

public sealed class GetTripsHandler : IHandler<GetTripsQuery, GetTripsResult>
{
    private readonly IOrderRepository _orders;

    public GetTripsHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<GetTripsResult>> HandleAsync(GetTripsQuery request, CancellationToken cancellationToken)
    {
        if (request.SectionId <= 0)
            return Error.InvalidInput("section is required");

        var trips = await _orders.GetTripsAsync(request.SectionId, cancellationToken);
        return new GetTripsResult(trips);
    }
}
