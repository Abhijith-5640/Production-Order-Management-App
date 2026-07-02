namespace NexusProd.Api.Domain.Entities;

/// <summary>
/// A single (branch, trip, qty) row inside an <see cref="OrderItem"/>.
/// </summary>
internal sealed record FlatRowItemM(
    int ItemId,
    string Name,
    int StockMastId,
    decimal TotalQty,
    decimal Qty,
    string Branch,
    int PurSaleId,
    int TripId,
    int BrnchId,
    int PurTmpltId,
    string TripName,
    int TripSequence,
    string UnitName,
    string BillNoStr,
    int UnitDecml,
    string IsPosCompleted //Mysql Bool Conversion
);