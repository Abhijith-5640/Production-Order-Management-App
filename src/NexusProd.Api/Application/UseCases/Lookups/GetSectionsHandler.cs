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
            var sections = lookup.Sections.ToList();

            // Check if there are uncategorized items with active orders
            var hasUncategorizedItems = await _orders.HasUncategorizedOrdersAsync(cancellationToken);

            if (hasUncategorizedItems)
            {
                // Prepend "No Section" as the first entry with a virtual ID of -1
                sections.Insert(0, new SectionDto(SectionDto.NoSectionId, "No Section"));
            }

            return new GetSectionsResult(lookup.CategoryId, sections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSections failed");
            return Error.DatabaseError(ex.Message);
        }
    }
}
