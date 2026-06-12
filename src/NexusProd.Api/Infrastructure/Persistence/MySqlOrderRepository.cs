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

    public async Task<bool> CheckPendingOrdersAsync(int BrnchId, CancellationToken cancellationToken)
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
            u.Trip  AS TripId,
            u.BrnchId
        FROM (
            SELECT 
                i.itm_mast_id    AS ItemId,
                i.itm_mast_name  AS `Name`,
                s.stock_mast_id  AS StockMastId,
                bsd.sales_qty    AS Qty,
                b.brnch_nam      AS Branch,
                bs.pur_sale_id   AS BillId,
                bs.trip_no       AS Trip,
                bs.pur_brnch_id AS BrnchId
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
                bs.pur_brnch_id AS BrnchId
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

    public async Task<string> ExcludeItemAsync(
        int sectionId,
        int itemId,
        int stockMastId,
        int currentTripId,
        int? brnchId,
        IReadOnlyList<int> purSaleIds,
        CancellationToken cancellationToken)
    {
        if (purSaleIds is null || purSaleIds.Count == 0)
            throw new InvalidOperationException("No purSaleIds provided for exclusion.");

        await using var conn = await _factory.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            // Track masters we need to roll up at the end. The master rollup is the
            // single place that recomputes master totals from the detail rows.
            var mastersToRollup = new HashSet<(long SalesMastId, bool IsTransfer)>();
            int processed = 0, skipped = 0;
            string? nextTripNameFound = null;

            foreach (var purSaleId in purSaleIds.Distinct())
            {
                // (A) BS LOOKUP — pur_sale_id is the stable identity; the BS row points
                // at the master and tells us sale vs. transfer.
                var bs = await conn.QuerySingleOrDefaultAsync<Inv31065BsEntry?>(new CommandDefinition(
                    @"SELECT sales_mast_id              AS SalesMastId,
                             IFNULL(is_for_transfer, 0) AS IsTransfer,
                             trip_no                    AS TripNo,
                             sale_brnch_id              AS SaleBrnchId,
                             pur_brnch_id               AS PurBrnchId
                      FROM   inv31065bs
                      WHERE  pur_sale_id = @purSaleId",
                    new { purSaleId },
                    transaction: tx, cancellationToken: cancellationToken));

                if (bs is null)
                {
                    _logger.LogWarning("ExcludeItem: INV31065BS not found for pur_sale_id {PurSaleId}", purSaleId);
                    skipped++;
                    continue;
                }

                // If brnchId is provided, only process rows for that branch.
                if (brnchId.HasValue && bs.PurBrnchId != brnchId.Value)
                {
                    _logger.LogDebug("ExcludeItem: skipping pur_sale_id {PurSaleId} — pur_brnch_id {PurBrnchId} != requested brnchId {BrnchId}",
                        purSaleId, bs.PurBrnchId, brnchId.Value);
                    skipped++;
                    continue;
                }

                var salesMastId = bs.SalesMastId;
                var isTransfer = bs.IsTransfer != 0;
                var detailTbl = isTransfer ? "INV31066BSD" : "INV31066";
                mastersToRollup.Add((salesMastId, isTransfer));

                // (B) READ the source detail row BEFORE deleting it. We need to
                // preserve every numeric column so the rollover insert in the next
                // trip's bill carries the exact same values. No re-derivation here
                // — the row moves verbatim and the master rollup is the only
                // place totals are recomputed.
                var sourceRow = await conn.QuerySingleOrDefaultAsync<Inv31066DetailRow?>(new CommandDefinition(
                    $@"SELECT sales_qty   AS SalesQty,
                              sales_rate  AS SalesRate,
                              tax_per     AS TaxPer,
                              cgst_per    AS CgstPer,
                              sgst_per    AS SgstPer,
                              cess_per    AS CessPer,
                              grs_amt     AS GrsAmt,
                              tax_amt     AS TaxAmt,
                              cgst_amt    AS CgstAmt,
                              sgst_amt    AS SgstAmt,
                              cess_amt    AS CessAmt,
                              disc_amt    AS DiscAmt,
                              tot_amt     AS TotAmt
                       FROM   {detailTbl}
                       WHERE  sales_mast_id = @salesMastId
                         AND  stock_mast_id = @stockMastId
                       LIMIT  1",
                    new { salesMastId, stockMastId },
                    transaction: tx, cancellationToken: cancellationToken));

                if (sourceRow is null)
                {
                    _logger.LogWarning("ExcludeItem: {Table} row not found for sales_mast_id {MasterId} stock_mast_id {StockMastId}",
                        detailTbl, salesMastId, stockMastId);
                    skipped++;
                    continue;
                }

                // (C) DELETE the source detail row. Master rollup below will
                // recompute master totals from whatever detail rows remain.
                await conn.ExecuteAsync(new CommandDefinition(
                    $@"DELETE FROM {detailTbl}
                       WHERE sales_mast_id = @salesMastId
                         AND stock_mast_id = @stockMastId",
                    new { salesMastId, stockMastId },
                    transaction: tx, cancellationToken: cancellationToken));

                // (D) TRIP ROLLOVER — find the next active trip. If none, the
                // item is fully removed and the master rollup takes care of
                // the current bill's totals.
                var nextTrip = await conn.QuerySingleOrDefaultAsync<(int Id, string Name)?>(new CommandDefinition(
                    @"SELECT id   AS Id,
                             trip AS Name
                      FROM   trip
                      WHERE  id > @currentTripNo
                      ORDER BY id ASC
                      LIMIT 1",
                    new { currentTripNo = currentTripId },
                    transaction: tx, cancellationToken: cancellationToken));

                if (nextTrip is null)
                {
                    processed++;
                    continue;
                }

                nextTripNameFound ??= nextTrip.Value.Name;

                // (E) Find the receiving bill in the next trip for the same
                // pur_brnch_id / sale_brnch_id pair.
                var nextBs = await conn.QuerySingleOrDefaultAsync<Inv31065BsEntry?>(new CommandDefinition(
                    @"SELECT sales_mast_id              AS SalesMastId,
                             IFNULL(is_for_transfer, 0) AS IsTransfer,
                             trip_no                    AS TripNo,
                             sale_brnch_id              AS SaleBrnchId,
                             pur_brnch_id               AS PurBrnchId
                      FROM   inv31065bs
                      WHERE  pur_brnch_id  = @purBrnchId
                        AND  sale_brnch_id = @saleBrnchId
                        AND  trip_no       = @nextTripId
                      LIMIT 1",
                    new
                    {
                        purBrnchId = bs.PurBrnchId,
                        saleBrnchId = bs.SaleBrnchId,
                        nextTripId = nextTrip.Value.Id
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                if (nextBs is null)
                {
                    // No receiving bill in the next trip for this pur_brnch_id /
                    // sale_brnch_id pair. The item is being permanently excluded
                    // for this branch — flag the corresponding INV21085 row
                    // (matched on itm_mast_id + brnch_id + trip_no) as excluded
                    // so the source-of-truth reflects the exclusion. If no
                    // matching INV21085 row exists, treat it as an error and
                    // roll back the whole exclusion.
                    _logger.LogInformation(
                        "ExcludeItem: no receiving INV31065BS in next trip {NextTripId} for pur_brnch_id {PurBrnchId} sale_brnch_id {SaleBrnchId} — flagging INV21085.is_exclude for item {ItemId} brnch {PurBrnchId} trip {TripNo}",
                        nextTrip.Value.Id, bs.PurBrnchId, bs.SaleBrnchId, itemId, bs.PurBrnchId, currentTripId);

                    var excluded = await conn.ExecuteAsync(new CommandDefinition(
                        @"UPDATE INV21085
                          SET    is_exclude = 1
                          WHERE  itm_mast_id = @itemId
                            AND  brnch_id    = @purBrnchId
                            AND  trip_no     = @tripNo",
                        new
                        {
                            itemId,
                            purBrnchId = bs.PurBrnchId,
                            tripNo = currentTripId,
                        },
                        transaction: tx, cancellationToken: cancellationToken));

                    if (excluded == 0)
                    {
                        throw new InvalidOperationException(
                            $"INV21085 row not found for item {itemId}, brnch {bs.PurBrnchId}, trip {currentTripId}. Cannot mark exclusion.");
                    }

                    processed++;
                    continue;
                }

                var nextSalesMastId = nextBs.SalesMastId;
                var nextIsTransfer = nextBs.IsTransfer != 0;
                var nextDetailTbl = nextIsTransfer ? "INV31066BSD" : "INV31066";
                mastersToRollup.Add((nextSalesMastId, nextIsTransfer));

                // (F) INSERT into the next trip's bill with the exact values
                // captured from the source row. No recalculation of the detail
                // row's amounts — the master rollup (step G) is the single
                // source of truth for master totals.
                await conn.ExecuteAsync(new CommandDefinition(
                    $@"INSERT INTO {nextDetailTbl}
                       (sales_mast_id, stock_mast_id,
                        sales_qty, sales_rate, tax_per, cgst_per, sgst_per, cess_per,
                        grs_amt, tax_amt, cgst_amt, sgst_amt, cess_amt, disc_amt, tot_amt)
                       VALUES
                       (@nextSalesMastId, @stockMastId,
                        @salesQty, @salesRate, @taxPer, @cgstPer, @sgstPer, @cessPer,
                        @grsAmt, @taxAmt, @cgstAmt, @sgstAmt, @cessAmt, @discAmt, @totAmt)",
                    new
                    {
                        nextSalesMastId,
                        stockMastId,
                        salesQty = sourceRow.SalesQty,
                        salesRate = sourceRow.SalesRate,
                        taxPer = sourceRow.TaxPer,
                        cgstPer = sourceRow.CgstPer,
                        sgstPer = sourceRow.SgstPer,
                        cessPer = sourceRow.CessPer,
                        grsAmt = sourceRow.GrsAmt,
                        taxAmt = sourceRow.TaxAmt,
                        cgstAmt = sourceRow.CgstAmt,
                        sgstAmt = sourceRow.SgstAmt,
                        cessAmt = sourceRow.CessAmt,
                        discAmt = sourceRow.DiscAmt,
                        totAmt = sourceRow.TotAmt,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                // (F.1) INV21085 TRIP MIGRATION — the source-of-truth row that
                // scheduled this item for the current trip must be retargeted
                // to the new trip. If a row already exists in INV21085 for
                // (item, branch, newTrip) we leave it alone and only update the
                // current-trip row. Otherwise we update the current-trip row's
                // trip_no to the new trip. Either way, the item is now bound
                // to the receiving bill's trip.
                var existingNewTrip = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                    @"SELECT 1
                      FROM   INV21085
                      WHERE  itm_mast_id = @itemId
                        AND  brnch_id    = @purBrnchId
                        AND  trip_no     = @newTripNo
                      LIMIT 1",
                    new
                    {
                        itemId,
                        purBrnchId = bs.PurBrnchId,
                        newTripNo = nextTrip.Value.Id,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                if (existingNewTrip is null)
                {
                    var migrated = await conn.ExecuteAsync(new CommandDefinition(
                        @"UPDATE INV21085
                          SET    trip_no = @newTripNo
                          WHERE  itm_mast_id = @itemId
                            AND  brnch_id    = @purBrnchId
                            AND  trip_no     = @currentTripNo",
                        new
                        {
                            newTripNo = nextTrip.Value.Id,
                            itemId,
                            purBrnchId = bs.PurBrnchId,
                            currentTripNo = currentTripId,
                        },
                        transaction: tx, cancellationToken: cancellationToken));

                    if (migrated == 0)
                    {
                        throw new InvalidOperationException(
                            $"INV21085 row not found for item {itemId}, brnch {bs.PurBrnchId}, trip {currentTripId}. Cannot migrate trip.");
                    }

                    _logger.LogInformation(
                        "ExcludeItem: migrated INV21085 trip for item {ItemId} brnch {PurBrnchId} from trip {CurrentTrip} to trip {NewTrip}",
                        itemId, bs.PurBrnchId, currentTripId, nextTrip.Value.Id);
                }
                else
                {
                    _logger.LogInformation(
                        "ExcludeItem: INV21085 already has a row for item {ItemId} brnch {PurBrnchId} on trip {NewTrip} — leaving the current-trip row in place",
                        itemId, bs.PurBrnchId, nextTrip.Value.Id);
                }

                processed++;
            }

            // (G) MASTER ROLLUP — the single place that recomputes master totals
            // from the detail rows (after the deletes and inserts above).
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

            if (processed == 0)
                return $"Excluded 0 of {purSaleIds.Count} pur_sale rows (none matched INV31065BS).";

            return nextTripNameFound is null
                ? $"Excluded {processed} of {purSaleIds.Count} from trip {currentTripId}. No next trip — items removed."
                : $"Excluded {processed} of {purSaleIds.Count} from trip {currentTripId}. Rolled over to {nextTripNameFound}.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExcludeItemAsync failed for sectionId {SectionId} itemId {ItemId} stockMastId {StockMastId} trip {Trip} brnchId {BrnchId}",
                sectionId, itemId, stockMastId, currentTripId, brnchId);
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<(int PurSaleId, int DistinctCount)?> FindSingleItemBillAsync(
        IReadOnlyList<int> purSaleIds,
        int stockMastId,
        CancellationToken cancellationToken)
    {
        if (purSaleIds is null || purSaleIds.Count == 0)
            return null;

        await using var conn = await _factory.OpenAsync(cancellationToken);

        // For each purSaleId, look up INV31065BS to get (sales_mast_id, is_for_transfer).
        // Then count distinct stock_mast_id values in the matching detail table
        // (INV31066 for sale, INV31066BSD for transfer). If any bill's distinct
        // count is exactly 1 — i.e. it carries only the requested stockMastId —
        // return that purSaleId so the caller can block the exclusion.
        //
        // The purSaleId loop is per-row because the count is per-bill and each
        // purSaleId maps to its own bill (sales_mast_id, is_for_transfer pair).
        foreach (var purSaleId in purSaleIds.Distinct())
        {
            var bs = await conn.QuerySingleOrDefaultAsync<(long SalesMastId, sbyte IsTransfer)?>(new CommandDefinition(
                @"SELECT sales_mast_id              AS SalesMastId,
                         IFNULL(is_for_transfer, 0) AS IsTransfer
                  FROM   inv31065bs
                  WHERE  pur_sale_id = @purSaleId
                  LIMIT 1",
                new { purSaleId },
                cancellationToken: cancellationToken));

            if (bs is null)
            {
                // purSaleId not found in BS — let the exclusion path surface that
                // warning; not the guard's concern here.
                continue;
            }

            var (salesMastId, isTransferRaw) = bs.Value;
            var detailTbl = isTransferRaw != 0 ? "INV31066BSD" : "INV31066";

            var distinctCount = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                $@"SELECT COUNT(DISTINCT stock_mast_id)
                   FROM {detailTbl}
                   WHERE sales_mast_id = @salesMastId",
                new { salesMastId },
                cancellationToken: cancellationToken)) ?? 0;

            if (distinctCount == 1)
            {
                // Confirm the single row in the bill is in fact the stockMastId
                // we are trying to exclude. If the bill has one row but it is a
                // different stock_mast_id, the exclusion would not empty the
                // bill and is safe.
                var matchesRequested = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
                    $@"SELECT COUNT(*)
                       FROM {detailTbl}
                       WHERE sales_mast_id = @salesMastId
                         AND stock_mast_id = @stockMastId",
                    new { salesMastId, stockMastId },
                    cancellationToken: cancellationToken)) ?? 0;

                if (matchesRequested > 0)
                    return (purSaleId, distinctCount);
            }
        }

        return null;
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

    private sealed class Inv31065BsEntry
    {
        public long SalesMastId { get; set; }
        public sbyte IsTransfer { get; set; }
        public int TripNo { get; set; }
        public int SaleBrnchId { get; set; }
        public int PurBrnchId { get; set; }
    }

    private sealed class Inv31066DetailRow
    {
        public decimal SalesQty { get; set; }
        public decimal SalesRate { get; set; }
        public decimal? TaxPer { get; set; }
        public decimal CgstPer { get; set; }
        public decimal SgstPer { get; set; }
        public decimal CessPer { get; set; }
        public decimal GrsAmt { get; set; }
        public decimal TaxAmt { get; set; }
        public decimal CgstAmt { get; set; }
        public decimal SgstAmt { get; set; }
        public decimal CessAmt { get; set; }
        public decimal DiscAmt { get; set; }
        public decimal TotAmt { get; set; }
    }
}
