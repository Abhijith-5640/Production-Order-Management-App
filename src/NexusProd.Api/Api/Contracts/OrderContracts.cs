using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Api.Contracts;

public sealed record DistributionDto(
    int PurSaleId,
    string Branch,
    int BrnchId,
    int Trip,
    decimal Qty,
    IReadOnlyList<Trip> availableTrips,
    string BillNoStr);

public sealed record OrderItemDto(
    int Id,
    int StockMastId,
    decimal TotalQty,
    string Name,
    string Unit,
    int UnitDecml,
    bool IsCompleted,
    IReadOnlyList<DistributionDto> Distribution);

public sealed record OrdersResponse(IReadOnlyList<OrderItemDto> Orders);

public sealed record CheckPendingResponse(bool PendingExist);

public sealed record GenerateInvoicesRequest(int UserId, int BrnchId, int UserCounterId);
public sealed record GenerateInvoicesResponse(bool Success, string Message, int InvoiceCount);

public sealed record UpdateOrderRequest(
    int ItemId,
    int Trip,
    IReadOnlyList<UpdateOrderDistributionDto> Distribution);

public sealed record UpdateOrderDistributionDto(
    int PurSaleId,
    int StockMastId,
    decimal OriginalQty,
    string Branch,
    decimal? Qty,
    int? TargetTrip = null);

public sealed record UpdateOrderResponse(bool Success, string Message);

public sealed record ExcludeEntry(
    int PurSaleId,
    decimal Qty,
    int? TargetTrip = null);

public sealed record ExcludeRequest(
    int SectionId,
    int ItemId,
    int CurrentTrip,
    int StockMastId,
    int? BrnchId,
    IReadOnlyList<ExcludeEntry> Entries);

public sealed record ExcludeResponse(bool Success, string Message);
