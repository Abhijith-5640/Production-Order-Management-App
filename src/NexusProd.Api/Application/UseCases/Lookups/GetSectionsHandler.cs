using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Lookups;

public sealed record GetSectionsQuery();
public sealed record GetSectionsResult(IReadOnlyList<string> Sections);

public sealed class GetSectionsHandler : IHandler<GetSectionsQuery, GetSectionsResult>
{
    private readonly IOrderRepository _orders;

    public GetSectionsHandler(IOrderRepository orders) => _orders = orders;

    public async Task<Result<GetSectionsResult>> HandleAsync(GetSectionsQuery request, CancellationToken cancellationToken)
    {
        var sections = await _orders.GetSectionsAsync(cancellationToken);
        return new GetSectionsResult(sections);
    }
}
