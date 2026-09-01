using NexusProd.Api.Api.Contracts;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.Abstractions;

public sealed record SectionsLookup(int CategoryId, IReadOnlyList<SectionDto> Sections);

/// <summary>
/// Repository abstraction for orders. The MySQL implementation is a 1:1
/// port of the raw SQL blocks from the original Node/Express bridge that
/// previously lived in <c>server/db/mysql_db.js</c> (that folder has been
/// removed; the .NET API is the sole backend).
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
    /// Checks if there are any active orders for items that do not have a valid section assignment.
    /// Returns true if uncategorized items exist in INV31065BS for today.
    /// </summary>
    Task<bool> HasUncategorizedOrdersAsync(CancellationToken cancellationToken);

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
    Task<string> UpdateInvoiceAsync(int itemId, int tripId, IReadOnlyList<DistributionEntry> newDistribution, int usrId, CancellationToken cancellationToken);

    /// <summary>
    /// Per-row exclude / rollover. For each <see cref="DistributionEntry"/>:
    /// looks up the master via <c>INV31065BS</c> (sale vs. transfer), and
    /// either DELETEs the matching detail row from <c>INV31066</c> /
    /// <c>INV31066BSD</c> (when <c>Qty == OriginalQty</c>) or UPDATEs the
    /// row in place with recomputed amounts (defensive, partial-qty path).
    /// When <c>TargetTrip</c> is supplied, the diff is also routed to that
    /// trip's bill — INSERT if no detail row exists there, otherwise UPDATE
    /// adding the diff. <c>INV21085</c> is intentionally not touched on
    /// either path. Master totals are rolled up via SUM subqueries at the
    /// end. Returns a "{updated} updated, {skipped} skipped, {carriedForward}
    /// carried forward, {carrySkipped} carry skipped" summary. Transactional.
    /// </summary>
    Task<string> ExcludeItemAsync(
        int sectionId,
        int itemId,
        int stockMastId,
        int currentTripId,
        int? brnchId,
        IReadOnlyList<DistributionEntry> entries,
        int usrId,
        CancellationToken cancellationToken);
}
