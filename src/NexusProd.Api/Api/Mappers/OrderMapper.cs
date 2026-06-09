using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Api.Mappers;

internal static class OrderMapper
{
    public static OrderItemDto ToDto(OrderItem item) => new(
        Id: item.Id,
        StockMastId: item.StockMastId,
        TotalQty: item.TotalQty ?? 0m,
        Name: item.Name,
        Unit: item.Unit,
        IsCompleted: item.IsCompleted,
        Distribution: item.Distribution
            .Select(d => new DistributionDto(d.PurSaleId, d.Branch, d.Trip, d.Qty ?? 0m))
            .ToList());
}
