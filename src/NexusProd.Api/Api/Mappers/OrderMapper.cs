using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Api.Mappers;

internal static class OrderMapper
{
    public static OrderItemDto ToDto(OrderItem item) => new(
        Id: item.Id,
        Name: item.Name,
        Unit: item.Unit,
        IsCompleted: item.IsCompleted,
        Distribution: item.Distribution
            .Select(d => new DistributionDto(d.Branch, d.Trip, (int)(d.Qty ?? 0)))
            .ToList());
}
