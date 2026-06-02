using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Application.Abstractions;

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
    Task<bool> CheckPendingOrdersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Groups <c>order_distribution</c> rows by <c>(branch_id, trip_id)</c>,
    /// creates the matching <c>sales_master</c> and <c>sales_details</c>
    /// records, and flips <c>inv_gen = 1</c>. Transactional.
    /// </summary>
    Task<int> GenerateInvoicesAsync(int userId, CancellationToken cancellationToken);

    /// <summary>SELECT section_name FROM sections WHERE is_active = 1</summary>
    Task<IReadOnlyList<string>> GetSectionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Distinct trip names from <c>sales_master</c> joined to items in the
    /// given section. Same query as the Express version.
    /// </summary>
    Task<IReadOnlyList<string>> GetTripsAsync(int SecId, CancellationToken cancellationToken);

    /// <summary>
    /// Items + branch distribution for the given section/trip. The
    /// repository is responsible for grouping the flat SQL rows into
    /// <see cref="OrderItem"/> instances, including the
    /// <c>isCompleted</c> flag (true only when every distribution row
    /// has <c>is_completed = 1</c>).
    /// </summary>
    Task<IReadOnlyList<OrderItem>> GetOrdersAsync(string sectionName, string tripName, CancellationToken cancellationToken);

    /// <summary>
    /// Updates <c>qty</c>, <c>total</c>, and sets <c>is_completed = 1</c>
    /// for each (item, trip, branch) in <paramref name="newDistribution"/>.
    /// Recalculates <c>sales_master.total_value</c> for every affected invoice.
    /// Transactional.
    /// </summary>
    Task UpdateInvoiceAsync(int itemId, string tripName, IReadOnlyList<DistributionEntry> newDistribution, CancellationToken cancellationToken);

    /// <summary>
    /// Excludes the matching <c>sales_details</c> for the current trip
    /// (and optionally a single branch), and rolls the qty over to the
    /// next chronologically active trip's invoice (creating a new
    /// <c>sales_master</c> if needed). Returns the human-readable message
    /// the Express API returns ("Excluded ... Rolled over to ...").
    /// Transactional.
    /// </summary>
    Task<string> ExcludeItemAsync(string sectionName, int itemId, string currentTripName, string? branchName, CancellationToken cancellationToken);
}
