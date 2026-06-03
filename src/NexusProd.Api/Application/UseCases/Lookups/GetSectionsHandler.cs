using Microsoft.Extensions.Logging;
using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Application.Common;

namespace NexusProd.Api.Application.UseCases.Lookups;

public sealed record GetSectionsQuery();
public sealed record GetSectionsResult(int CategoryId, IReadOnlyList<SectionDto> Sections);

public sealed class GetSectionsHandler : IHandler<GetSectionsQuery, GetSectionsResult>
{
    private readonly IOrderRepository _orders;
    private readonly ILogger<GetSectionsHandler> _logger;

    public GetSectionsHandler(IOrderRepository orders, ILogger<GetSectionsHandler> logger)
    {
        _orders = orders;
        _logger = logger;
    }

    public async Task<Result<GetSectionsResult>> HandleAsync(GetSectionsQuery request, CancellationToken cancellationToken)
    {
        try
        {
            var lookup = await _orders.GetSectionsAsync(cancellationToken);
            return new GetSectionsResult(lookup.CategoryId, lookup.Sections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSections failed");
            return Error.DatabaseError(ex.Message);
        }
    }
}
