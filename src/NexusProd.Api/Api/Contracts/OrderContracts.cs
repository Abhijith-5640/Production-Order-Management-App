namespace NexusProd.Api.Api.Contracts;

public sealed record DistributionDto(string Branch, string Trip, int Qty);

public sealed record OrderItemDto(
    int Id,
    string Name,
    string Unit,
    bool IsCompleted,
    IReadOnlyList<DistributionDto> Distribution);

public sealed record OrdersResponse(IReadOnlyList<OrderItemDto> Orders);

public sealed record CheckPendingResponse(bool PendingExist);

public sealed record GenerateInvoicesRequest(int UserId);
public sealed record GenerateInvoicesResponse(bool Success, string Message, int InvoiceCount);

public sealed record UpdateOrderRequest(
    int ItemId,
    string Trip,
    IReadOnlyList<UpdateOrderDistributionDto> NewDistribution);

public sealed record UpdateOrderDistributionDto(string Branch, int Qty);

public sealed record UpdateOrderResponse(bool Success, string Message);

public sealed record ExcludeRequest(
    string Section,
    int ItemId,
    string CurrentTrip,
    string? Branch);

public sealed record ExcludeResponse(bool Success, string Message);
