using Dapper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using NexusProd.Api.Api.Contracts;
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
    private readonly ILogger<MySqlOrderRepository> _logger;

    public MySqlOrderRepository(MySqlConnectionFactory factory, ILogger<MySqlOrderRepository> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public async Task<bool> CheckPendingOrdersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _factory.OpenAsync(cancellationToken);
            const string sql = "SELECT COUNT(*) FROM order_distribution WHERE inv_gen = 0";
            var count = await conn.ExecuteScalarAsync<long>(new CommandDefinition(sql, cancellationToken: cancellationToken));
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckPendingOrdersAsync failed");
            throw;
        }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateInvoicesAsync failed for userId {UserId}", userId);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SectionsLookup> GetSectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await _factory.OpenAsync(cancellationToken);

            int CatId = await conn.QueryFirstAsync<int>(new CommandDefinition(
                @"SELECT CAST(val_data AS SIGNED) AS Sel
                  FROM INV21040
                  WHERE key_data = 'SECTION_CATEGORY_ID'
                  LIMIT 1;",
                cancellationToken: cancellationToken
            ));

            var sections = new List<SectionDto>();
            if (CatId > 0)
            {
                var rows = await conn.QueryAsync<SectionDto>(new CommandDefinition(
                    @"SELECT prdt_cat_val_id AS Id, prdt_cat_val_nam AS Name
                      FROM inv20005
                      WHERE prdt_catgry_id = @CatId
                      AND is_enable = 1",
                    new { CatId }, cancellationToken: cancellationToken));
                sections = rows.ToList();
            }

            return new SectionsLookup(CatId, sections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetSectionsAsync failed");
            throw;
        }
    }

    public async Task<IReadOnlyList<TripsM>> GetTripsAsync(int SecId, CancellationToken cancellationToken)
    {
        try
        {
            var TripsList = new List<TripsM>();
            await using var conn = await _factory.OpenAsync(cancellationToken);
            var rows = await conn.QueryAsync<TripsM>(new CommandDefinition(
                @"SELECT  id AS Id,
			              trip AS Trip
                  FROM Trip
                  ORDER BY trip_seq ASC",
                new { SecId }, cancellationToken: cancellationToken));
            TripsList = rows.ToList();
            return TripsList;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTripsAsync failed for sectionId {SecId}", SecId);
            throw;
        }
    }

    public async Task<IReadOnlyList<OrderItem>> GetOrdersAsync(int sectionId, int tripId, CancellationToken cancellationToken)
    {
        try
        {
            const string sql = @"
        SELECT 
            u.ItemId,
            u.`Name`,
            u.StockMastId,
            SUM(u.Qty) OVER (PARTITION BY u.StockMastId) AS TotalQty,
            u.Qty,
            u.Branch,
            u.BillId AS PurSaleId,
            u.Trip  AS TripId
        FROM (
            SELECT 
                i.itm_mast_id    AS ItemId,
                i.itm_mast_name  AS `Name`,
                s.stock_mast_id  AS StockMastId,
                bsd.sales_qty    AS Qty,
                b.brnch_nam      AS Branch,
                bs.pur_sale_id   AS BillId,
                bs.trip_no       AS Trip,
                bs.sale_brnch_id AS BrnchId
            FROM INV31065BS bs
            JOIN INV31065bsd bsm ON bs.sales_mast_id = bsm.sales_mast_id
            JOIN INV31066bsd bsd ON bsd.sales_mast_id = bsm.sales_mast_id
            JOIN INV21050 s      ON s.stock_mast_id = bsd.stock_mast_id
            JOIN INV21010 i      ON s.itm_mast_id = i.itm_mast_id
            JOIN CTGE1165pur b   ON bs.pur_brnch_id = b.brnch_id
            JOIN INV21013 pc     ON pc.itm_mast_id = i.itm_mast_id
                                AND pc.prdt_cat_id = (
                                                        SELECT CAST(val_data AS SIGNED) 
                                                        FROM INV21040 
                                                        WHERE key_data = 'SECTION_CATEGORY_ID' 
                                                        LIMIT 1
                                                    )
            WHERE CAST(bsm.sales_date AS DATE) = CAST(NOW() AS DATE)
              AND IFNULL(bs.is_for_transfer, 0) = 1
              AND bs.trip_no         = @tripId
              AND pc.prdt_cat_val_id = @sectionId

            UNION ALL

            SELECT 
                i.itm_mast_id    AS ItemId,
                i.itm_mast_name  AS `Name`,
                s.stock_mast_id  AS StockMastId,
                sd.sales_qty     AS Qty,
                b.brnch_nam      AS Branch,
                bs.pur_sale_id   AS BillId,
                bs.trip_no       AS Trip,
                bs.sale_brnch_id AS BrnchId
            FROM INV31065BS bs
            JOIN INV31065 sm     ON bs.sales_mast_id = sm.sales_mast_id
            JOIN INV31066 sd     ON sd.sales_mast_id = sm.sales_mast_id
            JOIN INV21050 s      ON s.stock_mast_id = sd.stock_mast_id
            JOIN INV21010 i      ON s.itm_mast_id = i.itm_mast_id
            JOIN CTGE1165pur b   ON bs.pur_brnch_id = b.brnch_id
            JOIN INV21013 pc     ON pc.itm_mast_id = i.itm_mast_id
                                AND pc.prdt_cat_id = (
                                                        SELECT CAST(val_data AS SIGNED) 
                                                        FROM INV21040 
                                                        WHERE key_data = 'SECTION_CATEGORY_ID' 
                                                        LIMIT 1
                                                    )
            WHERE CAST(sm.sales_date AS DATE) = CAST(NOW() AS DATE)
              AND IFNULL(bs.is_for_transfer, 0) = 0
              AND bs.trip_no         = @tripId
              AND pc.prdt_cat_val_id = @sectionId
        ) u
        ORDER BY u.`Name`, u.Branch, u.BillId";


            await using var conn = await _factory.OpenAsync(cancellationToken);
            var rows = (await conn.QueryAsync<FlatRowItemM>(new CommandDefinition(
                sql,
                new { sectionId, tripId }, cancellationToken: cancellationToken))).ToList();

            var byItem = new Dictionary<int, OrderItem>();
            foreach (var row in rows)
            {
                if (!byItem.TryGetValue(row.StockMastId, out var item))
                {
                    item = new OrderItem
                    {
                        Id = row.ItemId,
                        Name = row.Name,
                        StockMastId = row.StockMastId,
                        TotalQty = row.TotalQty,
                        // Unit = row.Unit,
                        IsCompleted = false,
                        Distribution = new List<DistributionEntry>()
                    };
                    byItem[row.StockMastId] = item;
                }
                item.Distribution.Add(new DistributionEntry
                {
                    Branch = row.Branch,
                    PurSaleId = row.PurSaleId,
                    Trip = row.TripId,
                    Qty = Convert.ToDecimal(row.Qty),
                    BrnchId = row.BrnchId,
                });
                // if (!ToBool(item.IsCompleted)) item = item with { IsCompleted = false };
                byItem[row.StockMastId] = item;
            }

            return byItem.Values.OrderBy(x => x.IsCompleted).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetOrdersAsync failed for sectionId {SectionId} trip {Trip}", sectionId, tripId);
            throw;
        }
    }

    public async Task<string> UpdateInvoiceAsync(int itemId, int tripId, IReadOnlyList<DistributionEntry> newDistribution, CancellationToken cancellationToken)
    {
        int updated = 0, skipped = 0;
        var mastersToRollup = new HashSet<(long SalesMastId, bool IsTransfer)>();

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var d in newDistribution)
            {
                // (A) DIFF FILTER — skip rows whose qty is unchanged from the
                // snapshot taken at modal-open. No DB work needed.
                if (d.Qty is decimal newQty && newQty == d.OriginalQty)
                {
                    skipped++;
                    continue;
                }

                // (B) BS LOOKUP — each pur_sale_id is the stable identity; the BS
                // row points at the master and tells us sale vs. transfer.
                // Join to CTGE1165 to get currency_decml for rounding precision.
                var bs = await conn.QuerySingleOrDefaultAsync<(long SalesMastId, sbyte IsTransfer, int? CurrencyDecml)?>(new CommandDefinition(
                    @"SELECT bs.sales_mast_id              AS SalesMastId,
                             IFNULL(bs.is_for_transfer, 0) AS IsTransfer,
                             br.curncy_decml             AS CurrencyDecml
                      FROM   inv31065bs bs
                      JOIN   ctge1165   br ON br.brnch_id = bs.sale_brnch_id
                      WHERE  bs.pur_sale_id = @purSaleId",
                    new { d.PurSaleId },
                    transaction: tx, cancellationToken: cancellationToken));

                if (bs is null)
                {
                    _logger.LogWarning("UpdateInvoice: INV31065BS not found for pur_sale_id {PurSaleId}", d.PurSaleId);
                    skipped++;
                    continue;
                }

                // Destructure to get master ID, transfer flag, and rounding precision
                var (salesMastId, isTransferRaw, currencyDecml) = bs.Value;
                int decimals = currencyDecml ?? 3;
                bool isTransfer = isTransferRaw != 0;
                long masterId = salesMastId;
                mastersToRollup.Add((masterId, isTransfer));

                var detailTbl = isTransfer ? "INV31066BSD" : "INV31066";

                // (C) DETAIL READ — read rate-percentage columns for recomputation.
                // Both INV31066 (sale) and INV31066BSD (transfer) have identical columns.
                var existing = await conn.QuerySingleOrDefaultAsync<Inv31066Row?>(new CommandDefinition(
                    $@"SELECT sales_qty   AS SalesQty,
                              sales_rate  AS SalesRate,
                              tax_per     AS TaxPer,
                              cgst_per    AS CgstPer,
                              sgst_per    AS SgstPer,
                              cess_per    AS CessPer
                       FROM {detailTbl}
                       WHERE sales_mast_id = @masterId
                         AND stock_mast_id = @stockMastId
                       LIMIT 1",
                    new { masterId, d.StockMastId },
                    transaction: tx, cancellationToken: cancellationToken));

                if (existing is null)
                {
                    _logger.LogWarning("UpdateInvoice: {Table} row not found for sales_mast_id {MasterId} stock_mast_id {StockMastId}",
                        detailTbl, masterId, d.StockMastId);
                    skipped++;
                    continue;
                }

                // (D) COMPUTE NEW VALUES — rate-based, no discount.
                // grs_amt = sales_rate * newSalesQty; cgst/sgst/cess derive directly from grs_amt.
                // tax_amt uses tax_per when present, else falls back to sum of cgst + sgst + cess.
                decimal newSalesQty = d.Qty ?? 0m;
                decimal newGrsAmt = Math.Round(existing.SalesRate * newSalesQty, decimals);
                decimal newCgst = Math.Round(newGrsAmt * (existing.CgstPer / 100m), decimals);
                decimal newSgst = Math.Round(newGrsAmt * (existing.SgstPer / 100m), decimals);
                decimal newCess = Math.Round(newGrsAmt * (existing.CessPer / 100m), decimals);
                decimal newTax = existing.TaxPer.HasValue
                    ? Math.Round(newGrsAmt * (existing.TaxPer.Value / 100m), decimals)
                    : newCgst + newSgst + newCess;
                decimal newTot = newGrsAmt + newTax;

                // (E) DETAIL UPDATE — no discount in this workflow.
                await conn.ExecuteAsync(new CommandDefinition(
                    $@"UPDATE {detailTbl}
                       SET sales_qty = @newSalesQty,
                           grs_amt   = @newGrsAmt,
                           cgst_amt  = @newCgst,
                           sgst_amt  = @newSgst,
                           cess_amt  = @newCess,
                           tax_amt   = @newTax,
                           tot_amt   = @newTot
                       WHERE sales_mast_id = @masterId
                         AND stock_mast_id = @stockMastId",
                    new
                    {
                        newSalesQty,
                        newGrsAmt,
                        newCgst,
                        newSgst,
                        newCess,
                        newTax,
                        newTot,
                        masterId,
                        d.StockMastId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                updated++;
            }

            // (F) MASTER ROLLUP — one UPDATE per unique (masterId, isTransfer).
            // INV31065BS has no total column in this schema (see
            // PROJECT_STRUCTURE.md), so the rollup below is the only master write.
            foreach (var (sm, isT) in mastersToRollup)
            {
                var masterTbl = isT ? "INV31065BSD" : "INV31065";
                var rollupSrc = isT ? "INV31066BSD" : "INV31066";

                await conn.ExecuteAsync(new CommandDefinition(
                    $@"UPDATE {masterTbl}
                       SET tot_grs_amt  = (SELECT COALESCE(SUM(grs_amt),  0) FROM {rollupSrc} WHERE sales_mast_id = @sm),
                           tot_tax_amt  = (SELECT COALESCE(SUM(tax_amt),  0) FROM {rollupSrc} WHERE sales_mast_id = @sm),
                           tot_discount = (SELECT COALESCE(SUM(disc_amt), 0) FROM {rollupSrc} WHERE sales_mast_id = @sm),
                           grand_total  = (SELECT COALESCE(SUM(tot_amt),  0) FROM {rollupSrc} WHERE sales_mast_id = @sm)
                       WHERE sales_mast_id = @sm",
                    new { sm },
                    transaction: tx, cancellationToken: cancellationToken));
            }

            await tx.CommitAsync(cancellationToken);
            return $"{updated} updated, {skipped} skipped";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UpdateInvoiceAsync failed for itemId {ItemId} trip {Trip}", itemId, tripId);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<string> ExcludeItemAsync(int sectionId, int itemId, int currentTripId, string? branchName, CancellationToken cancellationToken)
    {
        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            var trips = (await conn.QueryAsync<(int trip_id, string trip_name)>(new CommandDefinition(
                "SELECT trip_id, trip_name FROM trip_master WHERE is_active = 1 ORDER BY trip_id",
                transaction: tx, cancellationToken: cancellationToken))).ToList();
            var currentIndex = trips.FindIndex(t => t.trip_id == currentTripId);
            var nextTrip = currentIndex >= 0 && currentIndex < trips.Count - 1 ? trips[currentIndex + 1] : ((int trip_id, string trip_name)?)null;

            var details = (await conn.QueryAsync<ExcludeRow>(new CommandDefinition(
                branchName is null
                    ? @"SELECT sd.sales_detail_id, sd.sales_master_id, sd.qty, sd.price, sm.branch_id
                        FROM sales_details sd
                        JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id
                        JOIN trip_master tm ON sm.trip_id = tm.trip_id
                        JOIN items i ON sd.item_id = i.item_id
                        WHERE sd.item_id = @itemId AND tm.trip_id = @currentTripId AND i.section_id = @sectionId"
                    : @"SELECT sd.sales_detail_id, sd.sales_master_id, sd.qty, sd.price, sm.branch_id
                        FROM sales_details sd
                        JOIN sales_master sm ON sd.sales_master_id = sm.sales_master_id
                        JOIN trip_master tm ON sm.trip_id = tm.trip_id
                        JOIN branch_master bm ON sm.branch_id = bm.branch_id
                        JOIN items i ON sd.item_id = i.item_id
                        WHERE sd.item_id = @itemId AND tm.trip_id = @currentTripId AND bm.branch_name = @branchName AND i.section_id = @sectionId",
                new { itemId, currentTripId, branchName, sectionId },
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
                ? $"Excluded from trip {currentTripId}. Item removed completely as no next trip exists."
                : $"Excluded from trip {currentTripId}. Rolled over to {nextTrip!.Value.trip_name}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExcludeItemAsync failed for sectionId {SectionId} itemId {ItemId} trip {Trip} branch {Branch}", sectionId, itemId, currentTripId, branchName);
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

    private sealed class Inv31066Row
    {
        public decimal SalesQty { get; set; }
        public decimal SalesRate { get; set; }
        public decimal? TaxPer { get; set; }
        public decimal CgstPer { get; set; }
        public decimal SgstPer { get; set; }
        public decimal CessPer { get; set; }
    }
}
