namespace NexusProd.Api.Domain.Entities;

/// <summary>
/// A production-order item with its branch distribution for a given trip.
/// Built by grouping flat SQL rows in the repository layer.
/// </summary>
public sealed record OrderItem
{
    public int Id { get; init; }
    public int StockMastId { get; init; }
    public decimal? TotalQty { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public bool IsCompleted { get; init; }
    public List<DistributionEntry> Distribution { get; init; } = new();
}
