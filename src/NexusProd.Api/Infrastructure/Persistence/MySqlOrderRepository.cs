using Dapper;
using MySqlConnector;
using NexusProd.Api.Application.Abstractions;
using NexusProd.Api.Domain.Entities;

namespace NexusProd.Api.Infrastructure.Persistence;

/// <summary>
/// 1:1 port of the raw SQL blocks in the legacy Express server
/// (<c>server/db/mysql_db.js</c>). Every method preserves the exact
/// behavior of the original so the JSON the client receives is
/// byte-identical.
/// </summary>
public sealed class MySqlOrderRepository : IOrderRepository
{
    private readonly MySqlConnectionFactory _factory;

    public MySqlOrderRepository(MySqlConnectionFactory factory) => _factory = factory;

    public async Task<bool> CheckPendingOrdersAsync(CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        const string sql = "SELECT COUNT(*) FROM order_distribution WHERE inv_gen = 0";
        var count = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task<int> GenerateInvoicesAsync(int userId, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var groups = (await conn.QueryAsync<(int branch_id, int trip_id)>(new CommandDefinition(
                "SELECT DISTINCT branch_id, trip_id FROM order_distribution WHERE inv_gen = 0",
                transaction: tx, cancellationToken: cancellationToken))).ToList();

            if (groups.Count == 0)
            {
                await tx.CommitAsync(cancellationToken);
                return 0;
            }

            var maxNo = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT COALESCE(MAX(invoice_no), 0) FROM sales_master",
                transaction: tx, cancellationToken: cancellationToken)) ?? 0;

            foreach (var (branchId, tripId) in groups)
            {
                var items = (await conn.QueryAsync<(int item_id, int qty, decimal price)>(new CommandDefinition(
                    @"SELECT od.item_id, od.qty, i.price
                      FROM order_distribution od
                      JOIN items i ON od.item_id = i.item_id
                      WHERE od.branch_id = @branchId AND od.trip_id = @tripId AND od.inv_gen = 0",
                    new { branchId, tripId }, transaction: tx, cancellationToken: cancellationToken))).ToList();

                if (items.Count == 0) continue;

                decimal totalValue = 0;
                foreach (var (_, qty, price) in items)
                    totalValue += price * qty;

                maxNo++;
                var salesMasterId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                    @"INSERT INTO sales_master (branch_id, invoice_prefix, invoice_no, invoice_date, total_value, created_user, trip_id)
                      VALUES (@branchId, 'INV', @invoiceNo, NOW(), @totalValue, @userId, @tripId);
                      SELECT LAST_INSERT_ID();",
                    new { branchId, invoiceNo = maxNo, totalValue, userId, tripId },
                    transaction: tx, cancellationToken: cancellationToken));

                foreach (var (itemId, qty, price) in items)
                {
                    var totalItem = price * qty;
                    await conn.ExecuteAsync(new CommandDefinition(
                        @"INSERT INTO sales_details (sales_master_id, item_id, price, qty, total)
                          VALUES (@salesMasterId, @itemId, @price, @qty, @total)",
                        new { salesMasterId, itemId, price, qty, total = totalItem },
                        transaction: tx, cancellationToken: cancellationToken));
                }

                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE order_distribution SET inv_gen = 1 WHERE branch_id = @branchId AND trip_id = @tripId AND inv_gen = 0",
                    new { branchId, tripId },
                    transaction: tx, cancellationToken: cancellationToken));
            }

            await tx.CommitAsync(cancellationToken);
            return groups.Count;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetSectionsAsync(CancellationToken cancellationToken)
    {
        var SectionLists = new List<string>();

        await using var conn = await _factory.OpenAsync(cancellationToken);

        int CatId = await conn.QueryFirstAsync<int>(new CommandDefinition(
            @"SELECT CAST(val_data AS SIGNED) AS Sel
              FROM INV21040 
              WHERE key_data = 'SECTION_CATEGORY_ID'
              LIMIT 1;",
            cancellationToken: cancellationToken
        ));

        if (CatId > 0)
        {
            var rows = await conn.QueryAsync<string>(new CommandDefinition(
            @"SELECT prdt_cat_val_nam AS SectionNames
              FROM inv20005 
              WHERE prdt_catgry_id = @CatId 
              AND is_enable = 1",
            cancellationToken: cancellationToken));
            SectionLists = rows.ToList();
        }

        return SectionLists;
    }

    public async Task<IReadOnlyList<string>> GetTripsAsync(int SecId, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            @"SELECT DISTINCT tm.trip_name
              FROM sales_master sm
              JOIN sales_details sd ON sm.sales_master_id = sd.sales_master_id
              JOIN items i ON sd.item_id = i.item_id
              JOIN sections s ON i.section_id = s.section_id
              JOIN trip_master tm ON sm.trip_id = tm.trip_id
              WHERE s.section_name = @SecId
              ORDER BY tm.trip_name",
            new { SecId }, cancellationToken: cancellationToken));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<OrderItem>> GetOrdersAsync(string sectionName, string tripName, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        var rows = (await conn.QueryAsync<OrderRow>(new CommandDefinition(
            @"SELECT
                  i.item_id        AS Id,
                  i.item_name      AS Name,
                  i.unit           AS Unit,
                  sd.sales_detail_id AS SalesDetailId,
                  sd.qty           AS Qty,
                  bm.branch_name   AS Branch,
                  sd.is_completed  AS IsCompleted
              FROM items i
              JOIN sections s ON i.section_id = s.section_id
              JOIN sales_details sd ON i.item_id = sd.item_id
              JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id
              JOIN branch_master bm ON sm.branch_id = bm.branch_id
              JOIN trip_master tm ON sm.trip_id = tm.trip_id
              WHERE s.section_name = @sectionName AND tm.trip_name = @tripName",
            new { sectionName, tripName }, cancellationToken: cancellationToken))).ToList();

        var byItem = new Dictionary<int, OrderItem>();
        foreach (var row in rows)
        {
            if (!byItem.TryGetValue(row.Id, out var item))
            {
                item = new OrderItem
                {
                    Id = row.Id,
                    Name = row.Name,
                    Unit = row.Unit,
                    IsCompleted = true,
                    Distribution = new List<DistributionEntry>()
                };
                byItem[row.Id] = item;
            }
            item.Distribution.Add(new DistributionEntry
            {
                Branch = row.Branch,
                Trip = tripName,
                Qty = Convert.ToInt32(row.Qty)
            });
            if (!ToBool(row.IsCompleted)) item = item with { IsCompleted = false };
            byItem[row.Id] = item;
        }

        return byItem.Values.OrderBy(x => x.IsCompleted).ToList();
    }

    public async Task UpdateInvoiceAsync(int itemId, string tripName, IReadOnlyList<DistributionEntry> newDistribution, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var affected = new HashSet<int>();
            foreach (var dist in newDistribution)
            {
                var found = await conn.QuerySingleOrDefaultAsync<(int sales_detail_id, int sales_master_id)?>(new CommandDefinition(
                    @"SELECT sd.sales_detail_id, sd.sales_master_id
                      FROM sales_details sd
                      JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id
                      JOIN trip_master tm ON sm.trip_id = tm.trip_id
                      JOIN branch_master bm ON sm.branch_id = bm.branch_id
                      WHERE sd.item_id = @itemId AND tm.trip_name = @tripName AND bm.branch_name = @branch",
                    new { itemId, tripName, dist.Branch },
                    transaction: tx, cancellationToken: cancellationToken));

                if (found is null) continue;
                var (detailId, masterId) = found.Value;
                affected.Add(masterId);

                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE sales_details SET qty = @qty, total = price * @qty, is_completed = 1 WHERE sales_detail_id = @detailId",
                    new { qty = dist.Qty, detailId },
                    transaction: tx, cancellationToken: cancellationToken));
            }

            foreach (var masterId in affected)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    @"UPDATE sales_master sm
                      SET sm.total_value = (SELECT COALESCE(SUM(total), 0) FROM sales_details WHERE sales_master_id = @masterId)
                      WHERE sm.sales_master_id = @masterId",
                    new { masterId },
                    transaction: tx, cancellationToken: cancellationToken));
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<string> ExcludeItemAsync(string sectionName, int itemId, string currentTripName, string? branchName, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var trips = (await conn.QueryAsync<(int trip_id, string trip_name)>(new CommandDefinition(
                "SELECT trip_id, trip_name FROM trip_master WHERE is_active = 1 ORDER BY trip_id",
                transaction: tx, cancellationToken: cancellationToken))).ToList();
            var currentIndex = trips.FindIndex(t => t.trip_name == currentTripName);
            var nextTrip = currentIndex >= 0 && currentIndex < trips.Count - 1 ? trips[currentIndex + 1] : ((int trip_id, string trip_name)?)null;

            var details = (await conn.QueryAsync<ExcludeRow>(new CommandDefinition(
                branchName is null
                    ? @"SELECT sd.sales_detail_id, sd.sales_master_id, sd.qty, sd.price, sm.branch_id
                        FROM sales_details sd
                        JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id
                        JOIN trip_master tm ON sm.trip_id = tm.trip_id
                        WHERE sd.item_id = @itemId AND tm.trip_name = @currentTripName"
                    : @"SELECT sd.sales_detail_id, sd.sales_master_id, sd.qty, sd.price, sm.branch_id
                        FROM sales_details sd
                        JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id
                        JOIN trip_master tm ON sm.trip_id = tm.trip_id
                        JOIN branch_master bm ON sm.branch_id = bm.branch_id
                        WHERE sd.item_id = @itemId AND tm.trip_name = @currentTripName AND bm.branch_name = @branchName",
                new { itemId, currentTripName, branchName },
                transaction: tx, cancellationToken: cancellationToken))).ToList();

            if (details.Count == 0)
                throw new InvalidOperationException("No matching distribution found for exclusion.");

            var affected = new HashSet<int>();
            foreach (var d in details)
            {
                affected.Add(d.sales_master_id);
                await conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM sales_details WHERE sales_detail_id = @id",
                    new { id = d.sales_detail_id },
                    transaction: tx, cancellationToken: cancellationToken));

                if (nextTrip is null) continue;
                var (nextTripId, nextTripName) = ((int, string))nextTrip;
                var branchId = d.branch_id;

                var existingMaster = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                    "SELECT sales_master_id FROM sales_master WHERE branch_id = @branchId AND trip_id = @tripId",
                    new { branchId, tripId = nextTripId },
                    transaction: tx, cancellationToken: cancellationToken));

                int nextMasterId;
                if (existingMaster is not null)
                {
                    nextMasterId = existingMaster.Value;
                }
                else
                {
                    var nextNo = (await conn.ExecuteScalarAsync<long?>(new CommandDefinition(
                        "SELECT COALESCE(MAX(invoice_no), 0) FROM sales_master",
                        transaction: tx, cancellationToken: cancellationToken)) ?? 0) + 1;
                    nextMasterId = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
                        @"INSERT INTO sales_master (branch_id, invoice_prefix, invoice_no, invoice_date, total_value, created_user, trip_id)
                          VALUES (@branchId, 'INV', @nextNo, NOW(), 0, 1, @tripId);
                          SELECT LAST_INSERT_ID();",
                        new { branchId, nextNo, tripId = nextTripId },
                        transaction: tx, cancellationToken: cancellationToken));
                }

                var existingDetail = await conn.QuerySingleOrDefaultAsync<(int sales_detail_id, decimal qty)?>(new CommandDefinition(
                    "SELECT sales_detail_id, qty FROM sales_details WHERE sales_master_id = @masterId AND item_id = @itemId",
                    new { masterId = nextMasterId, itemId },
                    transaction: tx, cancellationToken: cancellationToken));

                if (existingDetail is not null)
                {
                    var (detailId, qty) = existingDetail.Value;
                    var newQty = qty + d.qty;
                    await conn.ExecuteAsync(new CommandDefinition(
                        "UPDATE sales_details SET qty = @newQty, total = price * @newQty WHERE sales_detail_id = @detailId",
                        new { newQty, detailId },
                        transaction: tx, cancellationToken: cancellationToken));
                }
                else
                {
                    var total = d.price * d.qty;
                    await conn.ExecuteAsync(new CommandDefinition(
                        @"INSERT INTO sales_details (sales_master_id, item_id, price, qty, total)
                          VALUES (@masterId, @itemId, @price, @qty, @total)",
                        new { masterId = nextMasterId, itemId, price = d.price, qty = d.qty, total },
                        transaction: tx, cancellationToken: cancellationToken));
                }

                await conn.ExecuteAsync(new CommandDefinition(
                    @"UPDATE sales_master sm
                      SET sm.total_value = (SELECT COALESCE(SUM(total), 0) FROM sales_details WHERE sales_master_id = @masterId)
                      WHERE sm.sales_master_id = @masterId",
                    new { masterId = nextMasterId },
                    transaction: tx, cancellationToken: cancellationToken));
            }

            foreach (var masterId in affected)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    @"UPDATE sales_master sm
                      SET sm.total_value = (SELECT COALESCE(SUM(total), 0) FROM sales_details WHERE sales_master_id = @masterId)
                      WHERE sm.sales_master_id = @masterId",
                    new { masterId },
                    transaction: tx, cancellationToken: cancellationToken));
            }

            await tx.CommitAsync(cancellationToken);

            return nextTrip is null
                ? $"Excluded from {currentTripName}. Item removed completely as no next trip exists."
                : $"Excluded from {currentTripName}. Rolled over to {nextTrip!.Value.trip_name}.";
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static bool ToBool(object? val) => val switch
    {
        null => false,
        bool b => b,
        byte bt => bt != 0,
        sbyte sbt => sbt != 0,
        short s => s != 0,
        ushort us => us != 0,
        int i => i != 0,
        uint ui => ui != 0,
        long l => l != 0,
        ulong ul => ul != 0,
        string str => !string.IsNullOrEmpty(str) && str != "0",
        _ => val.Equals(true)
    };

    private sealed class OrderRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public int SalesDetailId { get; set; }
        public decimal Qty { get; set; }
        public string Branch { get; set; } = string.Empty;
        public object? IsCompleted { get; set; }
    }

    private sealed class ExcludeRow
    {
        public int sales_detail_id { get; set; }
        public int sales_master_id { get; set; }
        public decimal qty { get; set; }
        public decimal price { get; set; }
        public int branch_id { get; set; }
    }
}
