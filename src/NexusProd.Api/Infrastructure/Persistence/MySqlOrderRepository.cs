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
            const string sql = @"
                SELECT COUNT(*)
                FROM   INV21085 od
                JOIN   INV21100 bt ON od.brnch_id = bt.purchase_brnch_id
                WHERE  bt.selling_brnch_id      = @brnchId
                  AND  bt.is_automatic          = 1
                  AND  CAST(od.dt AS DATE)      = CAST(DATE_ADD(NOW(), INTERVAL -1 DAY) AS DATE)
                  AND  IFNULL(od.is_billed,  0) = 0
                  AND  IFNULL(od.is_exclude, 0) = 0
                  AND  od.qty                   > 0";
            var count = await conn.ExecuteScalarAsync<long>(
                new CommandDefinition(sql, new { brnchId = BrnchId }, cancellationToken: cancellationToken));
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CheckPendingOrdersAsync failed for brnchId {BrnchId}", BrnchId);
            throw;
        }
    }

    public async Task<int> GenerateInvoicesAsync(int userId, int brnchId, int userCounterId, CancellationToken cancellationToken)
    {
        // (0) precheck — short-circuit when the source set is empty so the
        // happy path doesn't pay for a transaction it won't use.
        await using var conn = await _factory.OpenAsync(cancellationToken);
        const string precheckSql = @"
             SELECT COUNT(*)
             FROM   INV21085
             WHERE  stats IN ('D','O')
             AND  IFNULL(is_billed,  0) = 0
             AND  IFNULL(is_exclude, 0) = 0
             AND  CAST(dt AS DATE) = CASE stats
                                                WHEN 'D' THEN CAST(DATE_ADD(NOW(), INTERVAL -1 DAY) AS DATE)
                                                ELSE CAST(NOW() AS DATE)
                                     END;";
        var hasAny = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            precheckSql, cancellationToken: cancellationToken));
        if (hasAny == 0) return 0;

        // (1) env lookups — read-only, no transaction. Looked up by userId /
        // system so the request body doesn't need to carry them.
        var v_zeroTaxId = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT tax_id FROM INV21001 WHERE tax_per = 0 LIMIT 1;",
            cancellationToken: cancellationToken)) ?? 0;
        var v_taxKey = await conn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT tax_key FROM ctge1165 LIMIT 1;",
            cancellationToken: cancellationToken)) ?? string.Empty;
        var v_curDate = await conn.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT getAdjustTime();",
            cancellationToken: cancellationToken));
        var CurncyDecml = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            @"SELECT IFNULL(curncy_decml,3) AS CurncyDecml
              FROM CTGE1165 
              WHERE brnch_id = @brnchId;",
            new { brnchId = brnchId },
            cancellationToken: cancellationToken));
        var v_finyear = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(
            @"SELECT finyear_id FROM ctge1160
              WHERE CAST(IFNULL(@curDate, NOW()) AS DATE) BETWEEN from_date AND to_date
              LIMIT 1;",
            new { curDate = v_curDate },
            cancellationToken: cancellationToken));


        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            // (2) worklist — distinct (brnch_id, trip_no) groups over the
            // same source filter the precheck used.
            var groups = (await conn.QueryAsync<BillGroupRow>(new CommandDefinition(
                @"SELECT DISTINCT brnch_id AS BrnchId, trip_no AS TripNo
                  FROM   INV21085
                  WHERE  stats IN ('D','O')
                    AND  IFNULL(is_billed,  0) = 0
                    AND  IFNULL(is_exclude, 0) = 0
                    AND  CAST(dt AS DATE) = CASE stats
                                              WHEN 'D' THEN CAST(DATE_ADD(NOW(), INTERVAL -1 DAY) AS DATE)
                                              ELSE CAST(NOW() AS DATE)
                                            END
                  ORDER BY brnch_id, trip_no;",
                transaction: tx, cancellationToken: cancellationToken))).ToList();

            if (groups.Count == 0)
            {
                await tx.CommitAsync(cancellationToken);
                return 0;
            }

            int processed = 0;
            foreach (var g in groups)
            {

                #region ProductionTemplateFetch
                var PrdtTemplts = await conn.QuerySingleOrDefaultAsync<TempltSetRow>(new CommandDefinition(
                    @"SELECT  bt.prod_tmplt_id                           AS ProdTmpltId,
                                  bt.selling_brnch_id                    AS SellingBrnchId,
                                  bt.purchase_brnch_id                   AS PurchaseBrnchId,
                                  bt.selling_ledger_id                   AS SellingLedgerId,
                                  bt.purchase_ledger_id                  AS PurchaseLedgerId,
                                  IFNULL(bt.is_transfer, 0)              AS IsTransfer
                          FROM    INV21100 bt
                          WHERE   bt.selling_brnch_id = @sellingBrnchId 
                          AND bt.purchase_brnch_id = @brnchId
                          LIMIT 1;",
                    new
                    {
                        sellingBrnchId = brnchId,
                        brnchId = g.BrnchId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                if (PrdtTemplts is null) continue;
                #endregion

                #region BillConfigs
                var BillNoConfigs = await conn.QuerySingleOrDefaultAsync<BillNoSettingsRow>(new CommandDefinition(
                    @"SELECT bill_no_prfx                                   BillNoPrfx,
    							 auth_no AS                                 AuthNo,
    							 delim                                      Delim,
    							 ts.tariff_id                               TariffId,
    							 ts.pay_type_id                             PayTypeId,
    							 cs.counter_setings_id                      CounterSettingsId,
                                 IFNULL(tf.is_exlusive_tax, 0)              Exclusive
    					FROM inv21075 ts
    					JOIN inv21033 cvs ON cvs.vou_typ_id = ts.vou_typ_id
    					JOIN inv21032 cs ON cs.cashier_usr_id = @userId
    					JOIN inv21070 tf ON tf.tariff_id = ts.tariff_id
    					WHERE cvs.is_primary = 1
                        AND cvs.counter_id= @counterId
    					AND cvs.vou_typ_id = @vouTypId
    					AND cvs.brnch_id= @sellingBrnchId
    					AND ts.ledger_id= @sellingLedgerId;",
                    new
                    {
                        userId = userId,
                        counterId = userCounterId,
                        vouTypId = 10,
                        sellingBrnchId = PrdtTemplts.SellingBrnchId,
                        sellingLedgerId = PrdtTemplts.SellingLedgerId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                if (BillNoConfigs is null) continue;
                #endregion


                #region BillNoFetching
                long BillNo = 0;
                if (PrdtTemplts.IsTransfer)
                {
                    BillNo = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    @"SELECT MAX(bill_no) + 1
    				    FROM inv31065BSD s
    				    WHERE s. vou_typ_id IN (SELECT cvs.vou_typ_id
    				    						FROM inv21075 ts
    				    						JOIN inv21033 cvs ON cvs.vou_typ_id = ts.vou_typ_id
    				    						WHERE cvs.counter_id = @counterId
    				    						AND cvs.is_primary = 1
    				    						AND ts.ledger_id = @sellingLedgerId
    				    						AND cvs.brnch_id = @sellingBrnchId)
    				    AND s.finyear_id = @finyear
    				    AND s.brnch_id = @sellingBrnchId
    				    AND IFNULL(s.bill_prfx,'') = IFNULL(@billPrfx,'');",
                    new
                    {
                        counterId = userCounterId,
                        sellingLedgerId = PrdtTemplts.SellingLedgerId,
                        sellingBrnchId = PrdtTemplts.SellingBrnchId,
                        finyear = v_finyear,
                        billPrfx = BillNoConfigs.BillNoPrfx
                    },
                    transaction: tx, cancellationToken: cancellationToken));
                }
                else
                {
                    bool IsContinuous = await conn.ExecuteScalarAsync<bool>(new CommandDefinition(
                    @"SELECT IFNULL(i33.is_continuous, 0) AS IsContinuous
    		            FROM inv21033 i33
    		            JOIN inv21075 ts ON ts.vou_typ_id = i33.vou_typ_id 
    		            AND ts.brnch_id = i33.brnch_id
    		            WHERE i33.brnch_id = @sellingBrnchId
    		            AND i33.is_primary = 1
    		            AND i33.counter_id = @counterId
    		            AND ts.ledger_id = @sellingLedgerId
    		            LIMIT 1;",
                    new
                    {
                        counterId = userCounterId,
                        sellingLedgerId = PrdtTemplts.SellingLedgerId,
                        sellingBrnchId = PrdtTemplts.SellingBrnchId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                    if (IsContinuous)
                    {
                        BillNo = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                        @"SELECT bill_no + 1
    					    FROM inv31065 s
    					    WHERE s.vou_typ_id IN (
    					    			SELECT cvs.vou_typ_id
    					    			FROM inv21075 ts
    					    			JOIN inv21033 cvs ON cvs.vou_typ_id = ts.vou_typ_id
    					    			WHERE cvs.counter_id = @counterId
    					    			AND cvs.is_primary = 1
    					    			AND ts.ledger_id = @sellingLedgerId
    					    			AND cvs.brnch_id = @sellingBrnchId
    					    			)
    					    AND s.brnch_id = @sellingBrnchId
    					    AND IFNULL(s.bill_prfx,'') = IFNULL(@billPrfx,'')
    					    ORDER BY bill_no DESC
    					    LIMIT 1;  ",
                        new
                        {
                            counterId = userCounterId,
                            sellingLedgerId = PrdtTemplts.SellingLedgerId,
                            sellingBrnchId = PrdtTemplts.SellingBrnchId,
                            finyear = v_finyear,
                            billPrfx = BillNoConfigs.BillNoPrfx
                        },
                        transaction: tx, cancellationToken: cancellationToken));
                    }
                    else
                    {
                        BillNo = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                        @"SELECT MAX(bill_no) + 1
    					    FROM inv31065 s
    					    WHERE s. vou_typ_id IN (SELECT cvs.vou_typ_id
    					    		FROM inv21075 ts
    					    		JOIN inv21033 cvs ON cvs.vou_typ_id = ts.vou_typ_id
    					    		WHERE cvs.counter_id = @counterId
    					    		AND cvs.is_primary = 1
    					    		AND ts.ledger_id = @sellingLedgerId
    					    		AND cvs.brnch_id = @sellingBrnchId)
    					    AND s.finyear_id = @finyear
    					    AND s.brnch_id = @sellingBrnchId
    					    AND IFNULL(s.bill_prfx,'') = IFNULL(@billPrfx,'');",
                        new
                        {
                            counterId = userCounterId,
                            sellingLedgerId = PrdtTemplts.SellingLedgerId,
                            sellingBrnchId = PrdtTemplts.SellingBrnchId,
                            finyear = v_finyear,
                            billPrfx = BillNoConfigs.BillNoPrfx
                        },
                        transaction: tx, cancellationToken: cancellationToken));

                    }

                    if (BillNo <= 0)
                    {
                        BillNo = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                        @"select bill_no+1
                            from last_bill lb
                            WHERE lb.prfx = @billPrfx
    					    and lb.is_primary = 1
    					    and lb.finyear_id = @finyear
                            and lb.vou_typ_id=@vouTypId
                            LIMIT 1;",
                        new
                        {
                            finyear = v_finyear,
                            billPrfx = BillNoConfigs.BillNoPrfx,
                            vouTypId = 10
                        },
                        transaction: tx, cancellationToken: cancellationToken));

                        if (BillNo > 0)
                        {
                            var UpdateLastBill = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                            @"
                             
                             SET SQL_SAFE_UPDATES = 0;
    
                             UPDATE last_bill AS lb
    						 SET lb.bill_no = @BillNo
    						 WHERE lb.prfx = @billPrfx
    						 and lb.is_primary = 1
                             and lb.finyear_id = @finyear
                             and lb.vou_typ_id=@vouTypId;
                             
                             SET SQL_SAFE_UPDATES = 1;
                             
                             ",
                            new
                            {
                                finyear = v_finyear,
                                billPrfx = BillNoConfigs.BillNoPrfx,
                                vouTypId = 10,
                                BillNo = BillNo
                            },
                            transaction: tx, cancellationToken: cancellationToken));
                        }
                    }

                }

                if (BillNo <= 0)
                {
                    BillNo = 1;
                }
                #endregion

                if (PrdtTemplts is null || BillNoConfigs is null) continue;

                var isTransfer = PrdtTemplts.IsTransfer;
                var masterTbl = isTransfer ? "INV31065BSD" : "INV31065";
                var detailTbl = isTransfer ? "INV31066BSD" : "INV31066";
                var isExclusive = BillNoConfigs.Exclusive;
                var tariffId = BillNoConfigs.TariffId ?? 0;
                var payTypeId = BillNoConfigs.PayTypeId ?? 0;
                var counterSet = BillNoConfigs.CounterSettingsId ?? 0;
                var salesDate = v_curDate ?? DateTime.Now;

                // (4b) items into in-memory list (replaces tBillItemtemp).
                var items = (await conn.QueryAsync<BillItemRow>(new CommandDefinition(
                    @"SELECT s.stock_mast_id   AS StockMastId,
                             u.unit_id         AS UnitId,
                             CASE WHEN IFNULL(o.edit_qty, 0) > 0 THEN o.edit_qty
                                  ELSE o.qty END        AS Qty,
                             r.sale_rate       AS SalesRate,
                             t.tax_id          AS TaxId,
                             IFNULL(t.tax_per, 0)     AS TaxPer,
                             0                 AS TaxAmt,
                             tc.tax_id         AS CessId,
                             IFNULL(tc.tax_per, 0)    AS CessPer,
                             0                 AS CgstPer,
                             0                 AS SgstPer,
                             r.base_rate       AS BaseRate
                      FROM   INV21085 o
                      JOIN   inv21010 i   ON i.itm_mast_id = o.itm_mast_id
                      JOIN   inv21050 s   ON s.itm_mast_id = i.itm_mast_id
                      JOIN   inv21001 t   ON t.tax_id      = i.tax_id
                      JOIN   inv21071 r   ON r.stock_mast_id = s.stock_mast_id
                                          AND r.base_rate = s.mrp
                      JOIN   inv00000 u   ON u.unit_id     = i.unit_id
                                          AND u.unit_id     = r.unit_id
                      JOIN   inv21070 tf  ON tf.tariff_id  = r.tariff_id
                      LEFT  JOIN inv21001 tc ON tc.tax_id    = i.cess_tax_id
                      WHERE  s.brnch_id  = @sellingBrnchId
                      AND  tf.tariff_id = @tariffId
                      AND  r.stat       = 1
                      AND  i.stats      = 1
                      AND  s.stats      = 1
                      AND  r.sale_rate  > 0
                      AND  IFNULL(o.is_billed,  0) = 0
                      AND  IFNULL(o.is_exclude, 0) = 0
                      AND  o.brnch_id  = @brnchId
                      AND  o.trip_no   = @tripNo
                      AND  o.stats IN ('D','O')
                      AND  CAST(o.dt AS DATE) = CASE o.stats
                                                  WHEN 'D' THEN CAST(DATE_ADD(@curentDate, INTERVAL -1 DAY) AS DATE)
                                                  ELSE CAST(@curentDate AS DATE)
                                                END;",
                    new
                    {
                        sellingBrnchId = PrdtTemplts.SellingBrnchId,
                        tariffId,
                        brnchId = g.BrnchId,
                        tripNo = g.TripNo,
                        curentDate = v_curDate,
                    },
                    transaction: tx, cancellationToken: cancellationToken))).ToList();

                if (items.Count == 0) continue;


                // (4d) master insert + LAST_INSERT_ID. The two statements are
                // batched by MySqlConnector, so this is one round trip.
                var salesMastId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                    $@"INSERT INTO {masterTbl}
                        (tariff_id, bill_prfx, delim, bill_no, auth_no, sales_date,
                         sales_ledger_id, pay_type_id, brnch_id, finyear_id, vou_typ_id,
                         cashier_id, counter_settings_id, tot_grs_amt, tot_tax_amt,
                         grand_total, is_ex_tax, descr, edit_stats, is_uploaded,
                         is_primary, cgst_tot, sgst_tot, cess_tot, tax_type_id,
                         is_branch_sale, tot_discount,
                         trnspt_mode, trnspt_doc_date, vehicle_no, driver_name,
                         round_off, is_changed_credit, counter_id)
                        VALUES
                        (@tariffId, @billPrfx, @delim, @billNo, @authNo, @salesDate,
                         @sellingLedgerId, @payTypeId, @sellingBrnchId, @finyearId, @vouTypId,
                         @userId, @counterSettingsId, 0, 0, 0, @isExTax, '',
                         0, 0, 1, 0, 0, 0, @taxTypeId, 1, 0,
                         @trnsptMode, CAST(@salesDate AS DATE), @vehicleNo, @driverName,
                         0, 0, @counterId);
                        SELECT LAST_INSERT_ID();",
                    new
                    {
                        tariffId,
                        billPrfx = BillNoConfigs.BillNoPrfx,
                        delim = BillNoConfigs.Delim,
                        billNo = BillNo,
                        authNo = BillNoConfigs.AuthNo,
                        salesDate = v_curDate ?? DateTime.Now,
                        sellingLedgerId = PrdtTemplts.SellingLedgerId,
                        payTypeId,
                        sellingBrnchId = PrdtTemplts.SellingBrnchId,
                        finyearId = v_finyear,
                        vouTypId = 10,
                        userId,
                        counterSettingsId = counterSet,
                        isExTax = isExclusive ? 1 : 0,
                        taxTypeId = (v_taxKey == "GST") ? 3 : 1,
                        trnsptMode = "1",
                        vehicleNo = (string?)null,
                        driverName = (string?)null,
                        counterId = userCounterId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                // (4e) detail bulk insert — one multi-row VALUES per group.
                // Mirrors the procedure's `INSERT INTO inv31066 SELECT ... FROM tBillItemtemp`.
                // The CASE block in 4h overwrites tax/total amounts; the initial
                // values are simply Qty*SalesRate for grs_amt.
                var valueTuples = string.Join(",", items.Select((_, idx) =>
                    $"(@sm{idx}, @stockMastId{idx}, @unitId{idx}, @qty{idx}, @salesRate{idx}, " +
                    $"@taxId{idx}, @taxPer{idx}, @grsAmt{idx}, 0, 0, " +
                    $"@cessId{idx}, @cessPer{idx}, @baseRate{idx})"));
                var detailParams = new DynamicParameters();
                detailParams.Add("sm", salesMastId);
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    detailParams.Add($"sm{i}", salesMastId);
                    detailParams.Add($"stockMastId{i}", it.StockMastId);
                    detailParams.Add($"unitId{i}", it.UnitId);
                    detailParams.Add($"qty{i}", it.Qty);
                    detailParams.Add($"salesRate{i}", it.SalesRate);
                    detailParams.Add($"taxId{i}", it.TaxId);
                    detailParams.Add($"taxPer{i}", it.TaxPer);
                    detailParams.Add($"grsAmt{i}", Math.Round(it.Qty * it.SalesRate, CurncyDecml));
                    detailParams.Add($"cessId{i}", it.CessId);
                    detailParams.Add($"cessPer{i}", it.CessPer);
                    detailParams.Add($"baseRate{i}", it.BaseRate);
                }
                await conn.ExecuteAsync(new CommandDefinition(
                    $@"INSERT INTO {detailTbl}
                       (sales_mast_id, stock_mast_id, unit_id, sales_qty, sales_rate,
                        tax_id, tax_per, grs_amt, tax_amt, tot_amt,
                        cess_id, cess_per, base_rate)
                       VALUES {valueTuples};",
                    detailParams,
                    transaction: tx, cancellationToken: cancellationToken));

                // (4h) detail CASE UPDATE — the big per-row recompute. Mirrors
                // the procedure verbatim. Runs first so the master totals can
                // be derived by summing the now-populated detail columns below
                // (replaces the four separate aggregation round trips the
                // original port used).
                await conn.ExecuteAsync(new CommandDefinition(
                    $@"UPDATE {detailTbl} d
                        JOIN   inv21050 s  ON s.stock_mast_id = d.stock_mast_id
                        JOIN   inv21010 i  ON i.itm_mast_id   = s.itm_mast_id
                        JOIN   inv21001 t  ON t.tax_id        = i.tax_id
                        LEFT  JOIN inv21001 ct ON ct.tax_id    = i.cess_tax_id
                        SET    d.tot_amt = ROUND((
                                    CASE WHEN @isExclusive = 1
                                         THEN (d.sales_qty*IFNULL(d.sales_rate, 0)*IFNULL(t.tax_per,0)/100)
                                            + (d.sales_qty*IFNULL(d.sales_rate, 0))
                                         ELSE d.sales_qty*IFNULL(d.sales_rate, 0) END), {CurncyDecml}),
                               d.tax_amt = ROUND((
                                    CASE WHEN @isExclusive = 1
                                         THEN (d.sales_qty*IFNULL(d.sales_rate, 0)*IFNULL(t.tax_per,0)/100)
                                         ELSE (d.grs_amt
                                              - ((d.sales_qty*IFNULL(d.sales_rate, 0))*100)
                                                 / (100 + IFNULL(t.tax_per, 0) + IFNULL(ct.tax_per, 0))) END), {CurncyDecml}),
                               d.cess_id  = CASE WHEN @taxKey = 'GST'
                                                  THEN IFNULL(i.cess_tax_id, @zeroTaxId)
                                                  ELSE NULL END,
                               d.cess_per = CASE WHEN @taxKey = 'GST'
                                                  THEN IFNULL(ct.tax_per, NULL)
                                                  ELSE NULL END,
                               d.cess_amt = CASE WHEN @taxKey = 'GST'
                                                  THEN (d.sales_qty*IFNULL(d.sales_rate, 0)*IFNULL(ct.tax_per, 0)/100)
                                                  ELSE NULL END,
                               d.cgst_per = CASE WHEN @taxKey = 'GST'
                                                  THEN (IFNULL(t.tax_per, 0) / 2)
                                                  ELSE NULL END,
                               d.cgst_amt = CASE WHEN @taxKey = 'GST'
                                                  THEN (d.sales_qty*IFNULL(d.sales_rate, 0)*(IFNULL(t.tax_per, 0)/2)/100)
                                                  ELSE NULL END,
                               d.sgst_per = CASE WHEN @taxKey = 'GST'
                                                  THEN (IFNULL(t.tax_per, 0) / 2)
                                                  ELSE NULL END,
                               d.sgst_amt = CASE WHEN @taxKey = 'GST'
                                                  THEN (d.sales_qty*IFNULL(d.sales_rate, 0)*(IFNULL(t.tax_per, 0)/2)/100)
                                                  ELSE NULL END,
                               d.grs_amt = ROUND((
                                    CASE WHEN @isExclusive = 1
                                         THEN d.sales_qty*IFNULL(d.sales_rate, 0)
                                         ELSE (d.sales_qty*IFNULL(d.sales_rate, 0)*100)
                                            / (100 + IFNULL(t.tax_per, 0) + IFNULL(ct.tax_per, 0)) END), {CurncyDecml})
                        WHERE  d.sales_mast_id = @sm;",
                    new
                    {
                        isExclusive,
                        taxKey = v_taxKey,
                        zeroTaxId = v_zeroTaxId,
                        sm = salesMastId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                // (4f) totals — single round trip: sum the columns the detail
                // UPDATE just populated. Replaces the four separate
                // aggregations (v_total, v_cgstTot, v_sgstTot, v_cessTot,
                // v_grsTot) the original port issued against the raw
                // sales_qty/sales_rate/tax_per columns.
                var totals = await conn.QuerySingleAsync<DetailTotalsRow>(new CommandDefinition(
                    $@"SELECT COALESCE(SUM(grs_amt),  0) AS GrsTot,
                              COALESCE(SUM(tax_amt),  0) AS TotTax,
                              COALESCE(SUM(tot_amt),  0) AS GrandTotal,
                              COALESCE(SUM(cgst_amt), 0) AS CgstTot,
                              COALESCE(SUM(sgst_amt), 0) AS SgstTot,
                              COALESCE(SUM(cess_amt), 0) AS CessTot
                       FROM   {detailTbl}
                       WHERE  sales_mast_id = @sm;",
                    new { sm = salesMastId },
                    transaction: tx, cancellationToken: cancellationToken));

                var v_total = Math.Round(totals.GrandTotal, CurncyDecml);
                var v_grsTot = Math.Round(totals.GrsTot, CurncyDecml);
                var v_cgstTot = v_taxKey == "GST" ? Math.Round(totals.CgstTot, CurncyDecml) : 0m;
                var v_sgstTot = v_taxKey == "GST" ? Math.Round(totals.SgstTot, CurncyDecml) : 0m;
                var v_cessTot = v_taxKey == "GST" ? Math.Round(totals.CessTot, CurncyDecml) : 0m;
                var v_totalTax = v_total - v_grsTot;

                // (4g) master totals UPDATE.
                await conn.ExecuteAsync(new CommandDefinition(
                    $@"UPDATE {masterTbl}
                        SET    grand_total  = @vTotal,
                               tot_tax_amt  = @vTotalTax,
                               cgst_tot     = @vCgstTot,
                               sgst_tot     = @vSgstTot,
                               cess_tot     = @vCessTot,
                               tot_grs_amt  = @vGrsTot,
                               tax_type_id  = @taxTypeId
                        WHERE  sales_mast_id = @sm;",
                    new
                    {
                        vTotal = v_total,
                        vTotalTax = v_totalTax,
                        vCgstTot = v_cgstTot,
                        vSgstTot = v_sgstTot,
                        vCessTot = v_cessTot,
                        vGrsTot = v_grsTot,
                        taxTypeId = (v_taxKey == "GST") ? 3 : 1,
                        sm = salesMastId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                // (4i) BS row — the procedure's universal join row.
                await conn.ExecuteAsync(new CommandDefinition(
                    @"INSERT INTO INV31065BS
                        (trip_no, sale_brnch_id, sale_acc_ledger_id, pur_brnch_id, pur_acc_ledger_id,
                         brnch_id, usr_id, pur_template_id, is_for_transfer, sales_mast_id, createdDt)
                        VALUES
                        (@tripNo, @sellingBrnchId, @sellingLedgerId, @purchaseBrnchId, @purchaseLedgerId,
                         @brnchId, @userId, @prodTmpltId, @isTransfer, @salesMastId, NOW());",
                    new
                    {
                        tripNo = g.TripNo,
                        sellingBrnchId = PrdtTemplts.SellingBrnchId,
                        sellingLedgerId = PrdtTemplts.SellingLedgerId,
                        purchaseBrnchId = g.BrnchId,
                        purchaseLedgerId = PrdtTemplts.PurchaseLedgerId,
                        brnchId = g.BrnchId,
                        userId,
                        prodTmpltId = PrdtTemplts.ProdTmpltId ?? 0,
                        isTransfer,
                        salesMastId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                // (4j) INV21085 flag — matches the source filter the precheck used.
                await conn.ExecuteAsync(new CommandDefinition(
                    @"
                    SET SQL_SAFE_UPDATES = 0;
                    
                    UPDATE INV21085
                        SET    is_billed = 1
                        WHERE  brnch_id  = @brnchId
                          AND  trip_no   = @tripNo
                          AND  stats IN ('D','O')
                          AND  IFNULL(is_billed,  0) = 0
                          AND  IFNULL(is_exclude, 0) = 0
                          AND  CAST(dt AS DATE) = CASE stats
                                                    WHEN 'D' THEN CAST(DATE_ADD(NOW(), INTERVAL -1 DAY) AS DATE)
                                                    ELSE CAST(NOW() AS DATE)
                                                  END;
                    SET SQL_SAFE_UPDATES = 1;
                    
                    ",
                    new { brnchId = g.BrnchId, tripNo = g.TripNo },
                    transaction: tx, cancellationToken: cancellationToken));

                processed++;
            }

            await tx.CommitAsync(cancellationToken);
            return processed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateInvoicesAsync failed for userId {UserId} brnchId {BrnchId}", userId, brnchId);
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
                @"SELECT DISTINCT t.id AS Id,
                  				t.trip AS Trip
                  FROM Trip t
                  JOIN INV31065BS bsm ON bsm.trip_no = t.id
                  WHERE bsm.createdDt >= CURDATE()
                  AND bsm.createdDt < CURDATE() + INTERVAL 1 DAY
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
            u.BrnchId,
            u.PurTmpltId,
            t.trip AS TripName,
            t.trip_seq AS TripSequence,
            u.UnitName
        FROM (
            SELECT 
                i.itm_mast_id    AS ItemId,
                i.itm_mast_name  AS `Name`,
                s.stock_mast_id  AS StockMastId,
                bsd.sales_qty    AS Qty,
                b.brnch_nam      AS Branch,
                bs.pur_sale_id   AS BillId,
                bs.trip_no       AS Trip,
                bs.pur_brnch_id AS BrnchId,
                bs.pur_template_id AS PurTmpltId,
                un.symbol    AS UnitName
            FROM INV31065BS bs
            JOIN INV31065bsd bsm ON bs.sales_mast_id = bsm.sales_mast_id
            JOIN INV31066bsd bsd ON bsd.sales_mast_id = bsm.sales_mast_id
            JOIN INV21050 s      ON s.stock_mast_id = bsd.stock_mast_id
            JOIN INV21010 i      ON s.itm_mast_id = i.itm_mast_id
            JOIN INV00000 un      ON i.unit_id = un.unit_id
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
                bs.pur_brnch_id AS BrnchId,
                bs.pur_template_id AS PurTmpltId,
                un.symbol    AS UnitName
            FROM INV31065BS bs
            JOIN INV31065 sm     ON bs.sales_mast_id = sm.sales_mast_id
            JOIN INV31066 sd     ON sd.sales_mast_id = sm.sales_mast_id
            JOIN INV21050 s      ON s.stock_mast_id = sd.stock_mast_id
            JOIN INV21010 i      ON s.itm_mast_id = i.itm_mast_id
            JOIN inv00000 un      ON i.unit_id = un.unit_id
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
        JOIN Trip t ON t.id = u.Trip
        ORDER BY u.`Name`, u.Branch, u.BillId";


            await using var conn = await _factory.OpenAsync(cancellationToken);
            var rows = (await conn.QueryAsync<FlatRowItemM>(new CommandDefinition(
                sql,
                new { sectionId, tripId }, cancellationToken: cancellationToken))).ToList();

            // Available trips per (pur_template_id, pur_brnch_id) — every trip
            // that has any finalized INV31065BS row for this template + branch
            // pair. The frontend filters out the row's own trip; the rest
            // become candidates for carry-forward / exclude-rollover. Without
            // this lookup, availableTrips collapses to just the current trip
            // and the user has nothing to choose from.
            var branchPairKeys = rows
                .Where(x => x.PurTmpltId != 0 && x.BrnchId != 0)
                .Select(x => new { x.PurTmpltId, x.BrnchId })
                .Distinct()
                .ToList();

            var tripsByTemplate = new Dictionary<int, List<Trip>>();
            foreach (var key in branchPairKeys)
            {
                var trips = await conn.QueryAsync<Trip>(new CommandDefinition(
                    @"SELECT DISTINCT t.id        AS Id,
                                      t.trip      AS Name,
                                      t.trip_seq  AS TripSeq
                      FROM   Trip t
                      JOIN   inv31065bs bs ON bs.trip_no = t.id
                      WHERE  bs.pur_template_id = @purTmpltId
                        AND  bs.pur_brnch_id    = @purBrnchId
                        AND  IFNULL(bs.is_finalized, 0) = 0
                        AND  CAST(bs.createdDt AS DATE) = CAST(NOW() AS DATE)
                      ORDER BY t.trip_seq ASC",
                    new { purTmpltId = key.PurTmpltId, purBrnchId = key.BrnchId },
                    cancellationToken: cancellationToken));
                tripsByTemplate[key.PurTmpltId] = trips.ToList();
            }

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
                        Unit = row.UnitName,
                        IsCompleted = false,
                        Distribution = new List<DistributionEntry>()

                    };
                    byItem[row.StockMastId] = item;
                }

                tripsByTemplate.TryGetValue(row.PurTmpltId, out var availableTrips);

                // Filter available trips to those with a trip_seq greater than
                // the current row's trip_seq (i.e. later trips on the same day
                // for this template/branch). The current trip is excluded by
                // the strict greater-than comparison.
                var laterTrips = (availableTrips ?? new List<Trip>())
                    .Where(t => t.TripSeq > row.TripSequence)
                    .ToList();

                item.Distribution.Add(new DistributionEntry
                {
                    Branch = row.Branch,
                    PurSaleId = row.PurSaleId,
                    Trip = row.TripId,
                    Qty = Convert.ToDecimal(row.Qty),
                    BrnchId = row.BrnchId,
                    AvailableTrips = laterTrips
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
        int updated = 0, skipped = 0, carriedForward = 0, carrySkipped = 0;
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
                // Also fetch pur_brnch_id and sale_brnch_id — needed for the
                // carry-forward branch to locate the user-selected target trip's bill.
                var bs = await conn.QuerySingleOrDefaultAsync<(long SalesMastId, sbyte IsTransfer, int? CurrencyDecml, int PurBrnchId, int SaleBrnchId)?>(new CommandDefinition(
                    @"SELECT bs.sales_mast_id              AS SalesMastId,
                             IFNULL(bs.is_for_transfer, 0) AS IsTransfer,
                             br.curncy_decml             AS CurrencyDecml,
                             bs.pur_brnch_id             AS PurBrnchId,
                             bs.sale_brnch_id            AS SaleBrnchId
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

                // Destructure to get master ID, transfer flag, rounding precision, and branch pair.
                var (salesMastId, isTransferRaw, currencyDecml, purBrnchId, saleBrnchId) = bs.Value;
                int decimals = currencyDecml ?? 3;
                bool isTransfer = isTransferRaw != 0;
                long masterId = salesMastId;
                mastersToRollup.Add((masterId, isTransfer));

                var detailTbl = isTransfer ? "INV31066BSD" : "INV31066";

                // (C) DETAIL READ — read rate-percentage columns for recomputation.
                // Both INV31066 (sale) and INV31066BSD (transfer) have identical columns.
                // unit_id, tax_id, cess_id, base_rate are also fetched so we can
                // INSERT a fresh detail row in the target trip's bill if none exists.
                var existing = await conn.QuerySingleOrDefaultAsync<Inv31066Row?>(new CommandDefinition(
                    $@"SELECT sales_qty   AS SalesQty,
                              sales_rate  AS SalesRate,
                              tax_per     AS TaxPer,
                              cgst_per    AS CgstPer,
                              sgst_per    AS SgstPer,
                              cess_per    AS CessPer,
                              unit_id     AS UnitId,
                              tax_id      AS TaxId,
                              cess_id     AS CessId,
                              base_rate   AS BaseRate
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

                // (E.1) CARRY-FORWARD BRANCH — when the user has reduced qty AND
                // chosen a target trip, route the diff (originalQty - newQty) to
                // that trip's bill. If a detail row already exists in the target
                // bill for the same (sales_mast_id, stock_mast_id) we add the
                // diff to its sales_qty and recompute amounts. Otherwise we
                // INSERT a fresh detail row with rate/tax columns copied from
                // the source and sales_qty = diff.
                //
                // INV21085 is intentionally NOT touched here — partial carry
                // leaves both trips with a row. trip_no migration is only for
                // full-exclude (handled by ExcludeItemAsync).
                if (d.Qty is decimal q && d.OriginalQty > q
                    && d.TargetTrip.HasValue && d.TargetTrip.Value > 0
                    && d.TargetTrip.Value != tripId)
                {
                    decimal diff = d.OriginalQty - q;

                    // (E.1.a) Target BS lookup — same branch pair, target trip,
                    // must be finalized.
                    var targetBs = await conn.QuerySingleOrDefaultAsync<Inv31065BsEntry?>(new CommandDefinition(
                        @"SELECT sales_mast_id              AS SalesMastId,
                                 IFNULL(is_for_transfer, 0) AS IsTransfer,
                                 trip_no                    AS TripNo,
                                 sale_brnch_id              AS SaleBrnchId,
                                 pur_brnch_id               AS PurBrnchId
                          FROM   inv31065bs
                          WHERE  pur_brnch_id  = @purBrnchId
                            AND  sale_brnch_id = @saleBrnchId
                            AND  trip_no       = @targetTrip
                            AND  is_finalized = 0
                          LIMIT 1",
                        new { purBrnchId, saleBrnchId, targetTrip = d.TargetTrip.Value },
                        transaction: tx, cancellationToken: cancellationToken));

                    if (targetBs is null)
                    {
                        _logger.LogWarning(
                            "UpdateInvoice: target trip {TargetTrip} has no finalized INV31065BS for pur_brnch {PurBrnchId}/sale_brnch {SaleBrnchId} (pur_sale_id {PurSaleId}). Carry-forward skipped.",
                            d.TargetTrip.Value, purBrnchId, saleBrnchId, d.PurSaleId);
                        carrySkipped++;
                        continue;
                    }

                    long targetSalesMastId = targetBs.SalesMastId;
                    bool targetIsTransfer = targetBs.IsTransfer != 0;
                    var targetDetailTbl = targetIsTransfer ? "INV31066BSD" : "INV31066";

                    // (E.1.b) Degenerate: target resolved to the same master the
                    // source row lives in. Nothing to carry.
                    if (targetSalesMastId == masterId)
                    {
                        _logger.LogWarning(
                            "UpdateInvoice: target trip {TargetTrip} resolves to the source master for pur_sale_id {PurSaleId}. Carry-forward skipped.",
                            d.TargetTrip.Value, d.PurSaleId);
                        carrySkipped++;
                        continue;
                    }

                    mastersToRollup.Add((targetSalesMastId, targetIsTransfer));

                    // (E.1.c) Detail existence check on the target bill.
                    var targetRow = await conn.QuerySingleOrDefaultAsync<Inv31066Row?>(new CommandDefinition(
                        $@"SELECT sales_qty   AS SalesQty,
                                  sales_rate  AS SalesRate,
                                  tax_per     AS TaxPer,
                                  cgst_per    AS CgstPer,
                                  sgst_per    AS SgstPer,
                                  cess_per    AS CessPer,
                                  unit_id     AS UnitId,
                                  tax_id      AS TaxId,
                                  cess_id     AS CessId,
                                  base_rate   AS BaseRate
                           FROM {targetDetailTbl}
                           WHERE sales_mast_id = @targetSalesMastId
                             AND stock_mast_id = @stockMastId
                           LIMIT 1",
                        new { targetSalesMastId, d.StockMastId },
                        transaction: tx, cancellationToken: cancellationToken));

                    // Recompute amounts against the *target* row's rate columns
                    // when one exists, otherwise use the source snapshot.
                    var rates = targetRow ?? existing;
                    decimal targetNewQty = (rates.SalesQty) + diff;
                    decimal targetGrs = Math.Round(rates.SalesRate * targetNewQty, decimals);
                    decimal targetCgst = Math.Round(targetGrs * (rates.CgstPer / 100m), decimals);
                    decimal targetSgst = Math.Round(targetGrs * (rates.SgstPer / 100m), decimals);
                    decimal targetCess = Math.Round(targetGrs * (rates.CessPer / 100m), decimals);
                    decimal targetTax = rates.TaxPer.HasValue
                        ? Math.Round(targetGrs * (rates.TaxPer.Value / 100m), decimals)
                        : targetCgst + targetSgst + targetCess;
                    decimal targetTot = targetGrs + targetTax;

                    if (targetRow is not null)
                    {
                        // (E.1.d) Path A — detail exists: add diff, recompute.
                        await conn.ExecuteAsync(new CommandDefinition(
                            $@"UPDATE {targetDetailTbl}
                               SET sales_qty = @targetNewQty,
                                   grs_amt   = @targetGrs,
                                   cgst_amt  = @targetCgst,
                                   sgst_amt  = @targetSgst,
                                   cess_amt  = @targetCess,
                                   tax_amt   = @targetTax,
                                   tot_amt   = @targetTot
                               WHERE sales_mast_id = @targetSalesMastId
                                 AND stock_mast_id = @stockMastId",
                            new
                            {
                                targetNewQty,
                                targetGrs,
                                targetCgst,
                                targetSgst,
                                targetCess,
                                targetTax,
                                targetTot,
                                targetSalesMastId,
                                d.StockMastId,
                            },
                            transaction: tx, cancellationToken: cancellationToken));
                    }
                    else
                    {
                        // (E.1.e) Path B — no detail row: INSERT a fresh one with
                        // sales_qty = diff and amounts computed against the
                        // source row's rate/tax columns.
                        await conn.ExecuteAsync(new CommandDefinition(
                            $@"INSERT INTO {targetDetailTbl}
                               (sales_mast_id, stock_mast_id, unit_id, sales_qty, sales_rate,
                                tax_id, tax_per, grs_amt, tax_amt, tot_amt,
                                cess_id, cess_per, base_rate,
                                cgst_amt, sgst_amt, cess_amt, disc_amt)
                               VALUES
                               (@targetSalesMastId, @stockMastId, @unitId, @diff, @salesRate,
                                @taxId, @taxPer, @targetGrs, @targetTax, @targetTot,
                                @cessId, @cessPer, @baseRate,
                                @targetCgst, @targetSgst, @targetCess, 0)",
                            new
                            {
                                targetSalesMastId,
                                d.StockMastId,
                                unitId = existing.UnitId,
                                diff,
                                salesRate = existing.SalesRate,
                                taxId = existing.TaxId,
                                taxPer = existing.TaxPer,
                                targetGrs,
                                targetTax,
                                targetTot,
                                cessId = existing.CessId,
                                cessPer = existing.CessPer,
                                baseRate = existing.BaseRate,
                                targetCgst,
                                targetSgst,
                                targetCess,
                            },
                            transaction: tx, cancellationToken: cancellationToken));
                    }

                    carriedForward++;
                }

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
            return $"{updated} updated, {skipped} skipped, {carriedForward} carried forward, {carrySkipped} carry skipped";
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
            int? nextTripIdFound = null;

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

                // (D) TRIP ROLLOVER — find the next finalized receiving bill for
                // the same pur_brnch_id / sale_brnch_id pair, on a later trip
                // than the current one. This folds the old "next trip" +
                // "next BS" pair of queries into a single lookup against
                // inv31065bs. If none, the item is fully removed and the
                // master rollup takes care of the current bill's totals.
                var nextBs = await conn.QuerySingleOrDefaultAsync<Inv31065BsEntry?>(new CommandDefinition(
                    @"SELECT sales_mast_id              AS SalesMastId,
                             IFNULL(is_for_transfer, 0) AS IsTransfer,
                             trip_no                    AS TripNo,
                             sale_brnch_id              AS SaleBrnchId,
                             pur_brnch_id               AS PurBrnchId
                      FROM   inv31065bs
                      WHERE  pur_brnch_id    = @purBrnchId
                        AND  sale_brnch_id   = @saleBrnchId
                        AND  trip_no        <> @currentTripNo
                        AND  trip_no         > @currentTripNo
                        AND  is_finalized   <> 0
                      ORDER BY trip_no ASC
                      LIMIT 1",
                    new
                    {
                        purBrnchId = bs.PurBrnchId,
                        saleBrnchId = bs.SaleBrnchId,
                        currentTripNo = currentTripId,
                    },
                    transaction: tx, cancellationToken: cancellationToken));

                if (nextBs is null)
                {
                    // No receiving bill on a later finalized trip for this
                    // pur_brnch_id / sale_brnch_id pair. The item is being
                    // permanently excluded for this branch — flag the
                    // corresponding INV21085 row (matched on itm_mast_id +
                    // brnch_id + trip_no) as excluded so the source-of-truth
                    // reflects the exclusion. If no matching INV21085 row
                    // exists, treat it as an error and roll back the whole
                    // exclusion.
                    _logger.LogInformation(
                        "ExcludeItem: no later finalized INV31065BS for pur_brnch_id {PurBrnchId} sale_brnch_id {SaleBrnchId} — flagging INV21085.is_exclude for item {ItemId} brnch {PurBrnchId} trip {TripNo}",
                        bs.PurBrnchId, bs.SaleBrnchId, itemId, bs.PurBrnchId, currentTripId);

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

                nextTripIdFound ??= nextBs.TripNo;

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
                        newTripNo = nextBs.TripNo,
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
                            newTripNo = nextBs.TripNo,
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
                        itemId, bs.PurBrnchId, currentTripId, nextBs.TripNo);
                }
                else
                {
                    _logger.LogInformation(
                        "ExcludeItem: INV21085 already has a row for item {ItemId} brnch {PurBrnchId} on trip {NewTrip} — leaving the current-trip row in place",
                        itemId, bs.PurBrnchId, nextBs.TripNo);
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

            return nextTripIdFound is null
                ? $"Excluded {processed} of {purSaleIds.Count} from trip {currentTripId}. No next trip — items removed."
                : $"Excluded {processed} of {purSaleIds.Count} from trip {currentTripId}. Rolled over to trip {nextTripIdFound}.";
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
        public int UnitId { get; set; }
        public int? TaxId { get; set; }
        public int? CessId { get; set; }
        public decimal BaseRate { get; set; }
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

    // (GenerateInvoicesAsync port) — worklist row.
    private sealed class BillGroupRow
    {
        public int BrnchId { get; set; }
        public int TripNo { get; set; }
    }

    // (GenerateInvoicesAsync port) — single LEFT-JOIN row from INV21100 +
    // the four non-transfer config tables. The four non-transfer columns
    // come back NULL on transfer rows.
    private sealed class BillMasterRow
    {
        public TempltSetRow? TempltSetMast { get; set; }
        public BillNoSettingsRow? BillNoSettings { get; set; }


    }
    public sealed class TempltSetRow
    {
        public int? ProdTmpltId { get; set; }
        public int SellingBrnchId { get; set; }
        public int PurchaseBrnchId { get; set; }
        public int SellingLedgerId { get; set; }
        public int PurchaseLedgerId { get; set; }
        public bool IsTransfer { get; set; }
    }
    public sealed class BillNoSettingsRow
    {
        public string? BillNoPrfx { get; set; }
        public string? AuthNo { get; set; }
        public string? Delim { get; set; }
        public int? TariffId { get; set; }
        public int? PayTypeId { get; set; }
        public bool Exclusive { get; set; }
        public DateTime? SalesDate { get; set; }
        public int? CounterSettingsId { get; set; }
    }
    // (GenerateInvoicesAsync port) — in-memory item list. Mirrors the
    // columns the legacy procedure's tBillItemtemp held.
    private sealed class BillItemRow
    {
        public int StockMastId { get; set; }
        public int UnitId { get; set; }
        public decimal Qty { get; set; }
        public decimal SalesRate { get; set; }
        public int TaxId { get; set; }
        public decimal TaxPer { get; set; }
        public decimal TaxAmt { get; set; }
        public int CessId { get; set; }
        public decimal CessPer { get; set; }
        public decimal CgstPer { get; set; }
        public decimal SgstPer { get; set; }
        public decimal BaseRate { get; set; }
    }

    // (GenerateInvoicesAsync port) — single SUM() row from the detail table
    // after the (4h) CASE UPDATE has populated tot_amt/tax_amt/grs_amt/
    // cgst_amt/sgst_amt/cess_amt.
    private sealed class DetailTotalsRow
    {
        public decimal GrsTot { get; set; }
        public decimal TotTax { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal CgstTot { get; set; }
        public decimal SgstTot { get; set; }
        public decimal CessTot { get; set; }
    }
    private sealed class VehicleDetailRow
    {
        public string VehicleNo { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;

    }
}
