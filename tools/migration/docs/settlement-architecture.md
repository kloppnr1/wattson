# Settlement Migration Architecture

How settlements are extracted from Xellent (AXDB50) and migrated to WattsOn.

## 1. How Xellent Stores Billing Data

A settlement in Xellent is a billing period for a metering point:

```
FlexBillingHistoryTable          <- one row per billing period
  +-- FlexBillingHistoryLine[]   <- one row per hour
        |-- TimeValue            <- kWh consumed
        |-- CalculatedPrice      <- total electricity price (spot + ALL supplier margins)
        +-- PowerExchangePrice   <- spot price only
```

**The combined supplier margin** is implicit:
`margin per hour = kWh * (CalculatedPrice - PowerExchangePrice)`

This margin includes BOTH the primary product rate AND any addon product rates (e.g., "Groen stroem"). Xellent does not store per-product margin breakdowns in FlexBillingHistory.

### Supplier margin rates are in ExuRateTable

Individual product rates exist in `EXU_RATETABLE`, resolved via:

```
ExuDelpoint (metering point)
  -> ExuAgreementTable
    -> ExuContractPartTable (Productnum = "V Fordel U")
      -> ExuProductExtendTable (Producttype = "V Klima")
        -> InventTable (ItemType, ExuUseRateFromFlexPricing)
          -> ExuRateTable (Ratetype = "V Klima", Rate = 0.030)
```

**Product types:**
- **Primary product** (`ExuUseRateFromFlexPricing == 1`): SpotAddon model. The rate in ExuRateTable is the margin per kWh added on top of spot. Note: some primary products have zero rates in ExuRateTable (rates only exist in FlexBillingHistory).
- **Addon product** (`ItemType == 2, ExuUseRateFromFlexPricing == 0`): Additional per-kWh charges like "Groen stroem". Rates typically DO exist in ExuRateTable.

### Rate resolution (4-tier fallback)

For each product, ExuRateTable rates are resolved in priority order:
1. Generic rates: `Ratetype = Producttype, Productnum = ""`
2. Product-specific: `Ratetype = Producttype, Productnum = productNum`
3. Self-referencing: `Ratetype = productNum, Productnum = productNum`
4. Any rates for the type: `Ratetype = Producttype` (any Productnum)

See `XellentExtractionService.ExtractDistinctProductsAsync()`.

### DataHub tariffs

Tariffs (nettarif, systemtarif, etc.) are stored separately in:

```
PriceElementCheck (metering point assignment)
  -> PriceElementCheckData (charge assignment periods)
    -> PriceElementTable (charge descriptions)
    -> PriceElementRates (hourly or flat rates)
```

## 2. How WattsOn Structures Settlements

A WattsOn settlement has typed **lines**, each with a `Source`:

```csharp
// SettlementLineSource enum
DataHubCharge  = 0   // nettarif, systemtarif, etc. (matched by PriceId in corrections)
SpotPrice      = 1   // wholesale spot
SupplierMargin = 2   // supplier's margin/markup
```

For a SpotAddon product, `SettlementCalculator.Calculate()` produces:

```
Settlement
  |-- SpotPrice line:       kWh * hourly spot rate
  |-- SupplierMargin line:  kWh * margin rate  ("V Fordel U")    <- one per product
  |-- SupplierMargin line:  kWh * margin rate  ("Groen stroem")  <- one per product
  |-- DataHubCharge line:   nettarif
  |-- DataHubCharge line:   systemtarif
  +-- DataHubCharge line:   elafgift
```

The multi-margin overload (`SettlementCalculator.cs`) creates **one margin line per named product**:

```csharp
foreach (var (name, margin) in namedMargins)
    settlement.AddLine(SettlementLine.CreateMargin(settlement.Id, name, totalEnergy, margin.PriceDkkPerKwh));
```

### Corrections

Corrections compare original vs new settlement by matching `(Source, PriceId)`:

```csharp
var originalLine = originalSettlement.Lines
    .FirstOrDefault(l => l.Source == newLine.Source && l.PriceId == newLine.PriceId);
```

**Important:** All SupplierMargin lines have `PriceId = null`. When multiple margin lines exist, `FirstOrDefault` always matches the first one. This means corrections currently compare each new margin line against the same first original margin line. This is a known limitation for multi-product margins -- corrections will produce correct totals but incorrect per-line deltas. A future fix would need a secondary match key (e.g., Description or a new ProductId field).

## 3. Migration Endpoint

`POST /api/migration/settlements` (`MigrationEndpoints.cs`)

The endpoint receives:
- `spotAmountDkk` -> creates a `SpotPrice` line
- `marginAmountDkk` -> creates an aggregate `SupplierMargin` line (only if no `PRODUCT:` tariff lines)
- `tariffLines[]` -> routed by `ChargeId` prefix:
  - `PRODUCT:*` -> `SupplierMargin` (per-product margin from ExuRateTable)
  - `IsSubscription` -> `DataHubCharge` flat amount
  - Regular -> `DataHubCharge` per-kWh (with Price FK when available)

## 4. Migration Data Flow

### Extract phase (`XellentSettlementService.BuildSettlementsAsync`)

This service mirrors Xellent's CorrectionService logic. It bulk-loads all reference data, then iterates periods in-memory:

1. **FlexBillingHistory** -> billing periods + hourly lines
2. **Tariff assignments** -> `PriceElementCheck -> PriceElementCheckData -> PriceElementTable -> PriceElementRates`
3. **Addon product chain** -> `Delpoint -> Agreement -> ContractPart -> ProductExtend -> InventTable` (where `ItemType=2, UseRateFromFlexPricing=0`)
4. **Addon product rates** -> `ExuRateTable` for addon product types
5. **Primary product** -> `Delpoint -> Agreement -> ContractPart` (product name + contract dates)

Per settlement period, the service produces:
- `PRODUCT:{primaryProductName}` line = residual margin (FlexBillingHistory total minus addon amounts)
- `PRODUCT:{addonName}` lines = addon margins (from ExuRateTable rates)
- DataHub tariff lines (from PriceElementRates)
- `MarginAmountDkk = 0` (all margin is in PRODUCT: lines)
- `hourlyLines` = per-hour provenance from FlexBillingHistoryLine

### Push phase (`Program.cs` step 9)

Sends each settlement to `/api/migration/settlements` with `marginAmountDkk = 0`, `tariffLines` (including PRODUCT: entries), `hourlyLines`.

### Resulting migrated settlement in WattsOn

```
Settlement (status = Migrated)
  |-- SpotPrice line:       kWh * avg spot       (from FlexBillingHistory)
  |-- SupplierMargin line:  "V Fordel U"  = 300  (residual from FlexBillingHistory)
  |-- SupplierMargin line:  "Groen stroem" =  20  (from ExuRateTable)
  |-- DataHubCharge line:   nettarif              (from PriceElementRates)
  |-- DataHubCharge line:   systemtarif           (from PriceElementRates)
  +-- DataHubCharge line:   elafgift              (from PriceElementRates)
```

This mirrors WattsOn's native settlement structure.

### Why residual (not ExuRateTable) for the primary product?

Some primary products have zero rates in ExuRateTable (the audit showed `Extracted=0.000000, AvgBilled=0.030000` for "V Fordel U"). Using the FlexBillingHistory residual (total margin minus addon amounts) gives the ground-truth billed amount for the primary product, regardless of ExuRateTable coverage.

## 5. Historical Note: The Double-Counting Bug

Before the per-product margin fix, the endpoint created BOTH an aggregate margin line from `marginAmountDkk` (which included addon amounts) AND per-product `PRODUCT:` lines from addon products. This double-counted addon margins:

```
marginAmountDkk = 320 DKK (primary 300 + addon 20)
PRODUCT:Groen stroem tariffLine = 20 DKK

Endpoint created:
  SupplierMargin "Leverandoermargin" = 320 DKK  <- includes addon
  SupplierMargin "Groen stroem"      =  20 DKK  <- addon again
  Total margin = 340 DKK  <- WRONG, should be 320
```

Fixed by: (a) extracting a primary product `PRODUCT:` line for the residual margin, setting `marginAmountDkk = 0`, and (b) skipping the aggregate margin line in the endpoint when `PRODUCT:` lines are present.
