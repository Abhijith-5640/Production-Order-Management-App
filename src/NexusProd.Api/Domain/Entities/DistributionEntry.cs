namespace NexusProd.Api.Domain.Entities;

/// <summary>
/// A single (branch, trip, qty) row inside an <see cref="OrderItem"/>.
/// </summary>
public sealed class DistributionEntry
{
    public string Branch { get; init; } = string.Empty;
    public string Trip { get; init; } = string.Empty;
    public int Qty { get; init; }
}
