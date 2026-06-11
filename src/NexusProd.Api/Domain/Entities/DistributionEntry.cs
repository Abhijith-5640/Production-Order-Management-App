namespace NexusProd.Api.Domain.Entities;

/// <summary>
/// A single (branch, trip, qty) row inside an <see cref="OrderItem"/>.
/// </summary>
public sealed class DistributionEntry
{
    public string Branch { get; init; } = string.Empty;
    public int BrnchId { get; init; }
    public int Trip { get; init; }
    public decimal? Qty { get; init; }
    public int PurSaleId { get; init; }
    public int StockMastId { get; init; }
    public decimal OriginalQty { get; init; }
}
