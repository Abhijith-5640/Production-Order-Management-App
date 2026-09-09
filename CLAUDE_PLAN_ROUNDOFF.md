# Plan: Round-Off Implementation

## Objective
Implement round-off calculation in `GenerateInvoicesAsync`. The round-off value will be calculated based on branch-specific settings from `INV21040` and saved to the `INV31065`/`INV31065BSD` table's `round_off` column.

---

## Files to Modify

### `src/NexusProd.Api/Infrastructure/Persistence/MySqlOrderRepository.cs`

---

## Step-by-Step Implementation

### Step 1: Add Helper Class for Rounding Logic

Add a new private static class `RoundingHelper` at the bottom of `MySqlOrderRepository.cs` (after the closing brace of the main class):

```csharp
/// <summary>
/// Helper class for round-off calculations.
/// </summary>
private static class RoundingHelper
{
    /// <summary>
    /// Rounding settings fetched from INV21040.
    /// </summary>
    public sealed record RoundingSettings(decimal NearestOf, decimal Fairness, int RoundingMode, bool RoundingNeed);

    /// <summary>
    /// Fetches rounding settings for a branch from INV21040.
    /// </summary>
    public static async Task<RoundingSettings> GetRoundingSettingsAsync(
        MySqlConnection conn, int saleBrnchId, CancellationToken cancellationToken)
    {
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(new CommandDefinition(
            @"SELECT 
                MAX(CASE WHEN key_val = 'ROUNDING_COMPONENT' THEN val_data END) AS NearestOf,
                MAX(CASE WHEN key_val = 'ROUNDING_MODE' THEN CAST(val_data AS SIGNED) END) AS RoundingMode,
                MAX(CASE WHEN key_val = 'ROUNDING_NEED' THEN CAST(val_data AS SIGNED) END) AS RoundingNeed
              FROM INV21040
              WHERE brnch_id = @saleBrnchId
                AND key_val IN ('ROUNDING_COMPONENT', 'ROUNDING_MODE', 'ROUNDING_NEED')",
            new { saleBrnchId },
            cancellationToken: cancellationToken));

        return new RoundingSettings(
            NearestOf: row?.NearestOf != null ? decimal.Parse(row.NearestOf.ToString()) : 1.0m,
            Fairness: 0.0m, // Not used in current implementation
            RoundingMode: row?.RoundingMode ?? 0,
            RoundingNeed: (row?.RoundingNeed ?? 0) == 1
        );
    }

    /// <summary>
    /// Ultimate rounding function - rounds amount to nearest component based on mode.
    /// </summary>
    public static decimal UltimateRoundingFunction(decimal amountToRound, decimal nearstOf, int roundingMode)
    {
        try
        {
            if (nearstOf <= 0)
                return amountToRound;

            decimal integerPart = Math.Truncate(amountToRound);
            decimal decimalPart = amountToRound - integerPart;

            // No rounding required
            if (decimalPart == 0)
                return amountToRound;

            const decimal tolerance = 0.000001m;

            // Check whether decimalPart is exactly on a configured component
            decimal multiplier = decimalPart / nearstOf;
            bool isExactComponent =
                Math.Abs(multiplier - Math.Round(multiplier, 0)) < tolerance;

            // Apply rounding mode only when exactly on a component
            if (isExactComponent)
            {
                switch (roundingMode)
                {
                    case 0: // No Change
                        return amountToRound;

                    case 1: // Round Up
                        return Math.Ceiling(amountToRound);

                    case 2: // Round Down
                        return Math.Floor(amountToRound);

                    default:
                        return amountToRound;
                }
            }

            // Special business rule for .5 component
            if (nearstOf == 0.5m)
            {
                return decimalPart < 0.5m
                    ? Math.Floor(amountToRound)
                    : Math.Ceiling(amountToRound);
            }

            // Generic rounding for all other components
            decimal roundedMultiplier =
                Math.Round(multiplier, 0, MidpointRounding.AwayFromZero);

            decimal roundedDecimal = roundedMultiplier * nearstOf;

            // Carry to next integer when required
            if (roundedDecimal >= 1m)
            {
                integerPart += 1m;
                roundedDecimal = 0m;
            }

            return integerPart + roundedDecimal;
        }
        catch
        {
            return amountToRound;
        }
    }

    /// <summary>
    /// Calculates the round-off amount for a given total.
    /// </summary>
    public static decimal CalculateRoundOff(decimal totBilAmt, RoundingSettings settings)
    {
        if (!settings.RoundingNeed)
            return 0;

        if (totBilAmt == 0)
            return 0;

        decimal roundedVal = UltimateRoundingFunction(totBilAmt, settings.NearestOf, settings.RoundingMode);
        return Math.Round(roundedVal - totBilAmt, 3);
    }

    /// <summary>
    /// Calculates grand total including round-off.
    /// </summary>
    public static decimal CalculateGrandTotal(decimal totBilAmt, decimal roundOff)
    {
        return Math.Round(totBilAmt + roundOff, 3);
    }
}
```

---

### Step 2: Modify `GenerateInvoicesAsync` - Fetch Rounding Settings

Find where `CurncyDecml` is fetched (around line 160), and add rounding settings fetch:

```csharp
var CurncyDecml = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
    @"SELECT IFNULL(curncy_decml,3) AS CurncyDecml
      FROM CTGE1165 
      WHERE brnch_id = @brnchId;",
    new { brnchId = brnchId },
    cancellationToken: cancellationToken));

// NEW: Fetch rounding settings for the sale branch
var roundingSettings = await RoundingHelper.GetRoundingSettingsAsync(conn, PrdtTemplts.SellingBrnchId, cancellationToken);
```

---

### Step 3: Modify `GenerateInvoicesAsync` - Calculate Round-Off

Find the totals calculation section (around line 677) and add round-off calculation:

```csharp
var v_total = Math.Round(totals.GrandTotal, CurncyDecml);
var v_grsTot = Math.Round(totals.GrsTot, CurncyDecml);
var v_cgstTot = v_taxKey == "GST" ? Math.Round(totals.CgstTot, CurncyDecml) : 0m;
var v_sgstTot = v_taxKey == "GST" ? Math.Round(totals.SgstTot, CurncyDecml) : 0m;
var v_cessTot = v_taxKey == "GST" ? Math.Round(totals.CessTot, CurncyDecml) : 0m;
var v_totalTax = v_total - v_grsTot;

// NEW: Calculate round-off
var v_roundOff = RoundingHelper.CalculateRoundOff(v_total, roundingSettings);
var v_grandTotal = RoundingHelper.CalculateGrandTotal(v_total, v_roundOff);
```

---

### Step 4: Modify Master INSERT - Add round_off Column

Update the master INSERT to include `round_off` parameter:

```csharp
// In masterParams, add:
masterParams.Add("roundOff", v_roundOff);
```

Update the `masterColumns` for both branches:

```csharp
// For transfer (INV31065BSD):
@"tariff_id, bill_prfx, delim, bill_no, auth_no, sales_date,
    sales_ledger_id, pay_type_id, brnch_id, finyear_id, vou_typ_id,
    cashier_id, counter_settings_id, tot_grs_amt, tot_tax_amt,
    grand_total, is_ex_tax, descr, edit_stats, is_uploaded,
    is_primary, cgst_tot, sgst_tot, cess_tot, tax_type_id,
    tot_discount, round_off, is_changed_credit, counter_id"

// For sale (INV31065):
@"tariff_id, bill_prfx, delim, bill_no, auth_no, sales_date,
    sales_ledger_id, pay_type_id, brnch_id, finyear_id, vou_typ_id,
    cashier_id, counter_settings_id, tot_grs_amt, tot_tax_amt,
    grand_total, is_ex_tax, descr, edit_stats, is_uploaded,
    is_primary, cgst_tot, sgst_tot, cess_tot, tax_type_id,
    is_branch_sale, tot_discount,
    trnspt_mode, trnspt_doc_date, vehicle_no, driver_name,
    round_off, is_changed_credit, counter_id"
```

Update the `masterValues` for both branches:

```csharp
// For transfer (INV31065BSD) - replace the "0, 0" at the end:
$"... 0, @roundOff, 0, @counterId"

// For sale (INV31065) - replace the "0, 0" before @counterId:
$"... @trnsptMode, CAST(@salesDate AS DATE), @vehicleNo, @driverName,
    @roundOff, 0, @counterId"
```

---

### Step 5: Modify Master UPDATE - Add round_off

Find the totals UPDATE (around line 685) and add `round_off`:

```csharp
await conn.ExecuteAsync(new CommandDefinition(
    $@"UPDATE {masterTbl}
        SET    grand_total  = @vTotal,
               tot_tax_amt  = @vTotalTax,
               cgst_tot     = @vCgstTot,
               sgst_tot     = @vSgstTot,
               cess_tot     = @vCessTot,
               tot_grs_amt  = @vGrsTot,
               tax_type_id  = @taxTypeId,
               round_off    = @roundOff  -- NEW
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
        roundOff = v_roundOff,  // NEW
        sm = salesMastId,
    },
    transaction: tx, cancellationToken: cancellationToken));
```

---

## Summary of Changes

| Location | Change |
|----------|--------|
| Bottom of file | Add `RoundingHelper` class with `GetRoundingSettingsAsync`, `UltimateRoundingFunction`, `CalculateRoundOff` |
| After CurncyDecml fetch | Add `RoundingSettings` fetch from INV21040 |
| After totals calculation | Calculate `v_roundOff` using `RoundingHelper.CalculateRoundOff` |
| Master INSERT columns | Ensure `round_off` is in column list |
| Master INSERT values | Add `@roundOff` to parameter values |
| Master UPDATE | Add `round_off = @roundOff` to SET clause |

---

## Database Settings (INV21040)

The following settings will be fetched from `INV21040` per branch:

| key_val | Description | Example |
|---------|-------------|---------|
| `ROUNDING_COMPONENT` | Nearest value to round to | `1`, `0.5`, `0.1` |
| `ROUNDING_MODE` | Rounding mode (0=None, 1=Up, 2=Down) | `0`, `1`, `2` |
| `ROUNDING_NEED` | Whether rounding is enabled | `0` (disabled), `1` (enabled) |

---

## Behavior

1. If `ROUNDING_NEED = 0` or settings not found: `round_off = 0`
2. If `ROUNDING_NEED = 1` and total has decimals:
   - Calculate rounded value using `UltimateRoundingFunction`
   - `round_off = rounded_value - original_value`
3. `grand_total` remains unchanged (does NOT include round_off - this matches legacy behavior where round_off is stored separately)
