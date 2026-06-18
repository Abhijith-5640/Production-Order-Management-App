using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.Abstractions;

public sealed record SectionsLookup(int CategoryId, IReadOnlyList<SectionDto> Sections);

/// <summary>
/// Repository abstraction for orders. All methods are 1:1 ports of the
/// raw SQL blocks in the legacy Express server (see
/// <c>server/db/mysql_db.js</c>).
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// <c>SELECT COUNT(*) FROM order_distribution WHERE inv_gen = 0</c>.
    /// </summary>
    Task<bool> CheckPendingOrdersAsync(int BrnchId, CancellationToken cancellationToken);

    /// <summary>
    /// 1:1 port of the legacy "generate the day's bills" MySQL procedure. Loads
    /// unbilled <c>INV21085</c> rows for <c>entry_type IN ('D','O')</c> on their
    /// respective date window (yesterday / today), groups by (brnch_id, trip_no),
    /// and for each group: resolves config from <c>INV21100</c> + 4 lookup
    /// tables, inserts a sales master (<c>inv31065</c> or <c>inv31065bsd</c>),
    /// bulk-inserts detail rows (<c>inv31066</c> or <c>inv31066bsd</c>),
    /// rolls the master totals up via four SUM aggregates, runs the per-row
    /// CASE update, writes the BS ledger row, and flips
    /// <c>INV21085.is_billed = 1</c>. Single transaction, single connection.
    /// Returns the number of (brnch_id, trip_no) groups processed; 0 when the
    /// source set is empty. <c>counter_id</c> is looked up from the user row.
    /// </summary>
    Task<int> GenerateInvoicesAsync(int userId, int brnchId, int userCounterId, CancellationToken cancellationToken);

    /// <summary>SELECT prdt_cat_val_id, prdt_cat_val_nam FROM inv20005 for the configured category</summary>
    Task<SectionsLookup> GetSectionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Distinct trip names from <c>sales_master</c> joined to items in the
    /// given section. Same query as the Express version.
    /// </summary>
    Task<IReadOnlyList<TripsM>> GetTripsAsync(int SecId, CancellationToken cancellationToken);

    /// <summary>
    /// Items + branch distribution for the given section/trip. The
    /// repository is responsible for grouping the flat SQL rows into
    /// <see cref="OrderItem"/> instances, including the
    /// <c>isCompleted</c> flag (true only when every distribution row
    /// has <c>is_completed = 1</c>).
    /// </summary>
    Task<IReadOnlyList<OrderItem>> GetOrdersAsync(int sectionId, int tripId, CancellationToken cancellationToken);

    /// <summary>
    /// Cascade update rooted at each distribution row's <c>pur_sale_id</c>:
    /// looks up the master via <c>INV31065BS</c> (sale vs. transfer),
    /// ratio-scales the matching detail row in <c>INV31066</c> /
    /// <c>INV31065</c> (or the BSD variants for transfer), and rolls the
    /// master totals up via SUM subqueries. Rows whose
    /// <c>Qty == OriginalQty</c> are skipped (no-op diff). Transactional.
    /// Returns a human-readable summary "{updated} updated, {skipped} skipped".
    /// </summary>
    Task<string> UpdateInvoiceAsync(int itemId, int tripId, IReadOnlyList<DistributionEntry> newDistribution, CancellationToken cancellationToken);

    /// <summary>
    /// Excludes the matching <c>sales_details</c> for the current trip
    /// (and optionally a single branch), and rolls the qty over to the
    /// next chronologically active trip's invoice (creating a new
    /// <c>sales_master</c> if needed). Returns the human-readable message
    /// the Express API returns ("Excluded ... Rolled over to ...").
    /// Transactional.
    /// </summary>
    Task<string> ExcludeItemAsync(
        int sectionId,
        int itemId,
        int stockMastId,
        int currentTripId,
        int? brnchId,
        IReadOnlyList<int> purSaleIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Guard for the exclude flow. For each <paramref name="purSaleIds"/>
    /// entry, looks up the matching <c>INV31065BS</c> row, resolves the
    /// <c>sales_mast_id</c> + <c>is_for_transfer</c> pair to the right
    /// detail table (<c>INV31066</c> for sale, <c>INV31066BSD</c> for
    /// transfer), and counts the distinct <c>stock_mast_id</c> values in
    /// that bill. Returns the first <c>(purSaleId, distinctCount)</c>
    /// pair whose distinct count is exactly 1 — meaning that bill carries
    /// only the requested <paramref name="stockMastId"/> and cannot be
    /// excluded. Returns <c>null</c> when every touched bill has at
    /// least one other <c>stock_mast_id</c> (safe to exclude).
    /// </summary>
    Task<(int PurSaleId, int DistinctCount)?> FindSingleItemBillAsync(
        IReadOnlyList<int> purSaleIds,
        int stockMastId,
        CancellationToken cancellationToken);
}
