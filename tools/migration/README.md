# WattsOn Migration Tool

CLI tool for migrating customer data from Xellent to WattsOn.

## Usage

```bash
dotnet run --project src/WattsOn.Migration.Cli -- \
  --accounts 458813 405013 512345 \
  --xellent-connection "Server=xel2012;Database=AXDB50_TEST;Trusted_Connection=True;TrustServerCertificate=True" \
  --wattson-url http://localhost:5100 \
  --supplier-gln 5790000000005 \
  --supplier-name "Verdo"
```

### Options

| Flag | Description | Required |
|------|-------------|----------|
| `--accounts` | Xellent account numbers to migrate | Yes |
| `--xellent-connection` | SQL Server connection string | Yes |
| `--wattson-url` | WattsOn API base URL (default: `http://localhost:5100`) | No |
| `--supplier-gln` | Supplier GLN (EAN number) | Yes |
| `--supplier-name` | Supplier name (default: `Verdo`) | No |
| `--include-timeseries` | Include historical time series | No |
| `--timeseries-start` | Time series start date (default: 2 years ago) | No |
| `--dry-run` | Extract and log but don't push to WattsOn | No |

### Migration Order

1. Supplier Identity (ensure exists)
2. Supplier Products (distinct product names from ContractParts)
3. Customers + Metering Points + Supplies
4. Supply Product Periods (product history per supply)
5. Supplier Margins (rate expansion from products)
6. Time Series (optional, historical consumption)

### Architecture

```
WattsOn.Migration.Cli          — CLI orchestrator (System.CommandLine)
WattsOn.Migration.Core         — Shared models (ExtractedCustomer, etc.)
WattsOn.Migration.XellentData  — Xellent DB access (EF Core + SQL Server)
WattsOn.Migration.WattsOnApi   — HTTP client for WattsOn /api/migration/* endpoints
```

### Known Limitations

- **Rate expansion**: Xellent stores a single flat rate per product/start date.
  We expand this into hourly `SupplierMargin` entries in WattsOn.
  **TODO**: Investigate whether Xellent has hourly rate differentiation
  (see `ExuPriceElementRates` hours 1-24). If so, extract hourly rates
  instead of expanding a flat rate.

- **Product-rate mapping**: Currently gets all generic rates, not product-specific.
  Needs refinement: `ExuProductExtendTable → Ratetype → ExuRateTable` chain.

- **Margin product ID lookup**: After creating products in WattsOn, we need
  to look up their IDs to attach margins. Needs product lookup API or
  return IDs from the migrate endpoint.

- **Price links**: Not yet extracted from Xellent (`ExuDelpointPriceElementR25046`).
  DataHub charges will typically be re-fetched from DataHub directly.

### Prerequisites

- WattsOn API running (with migration endpoints)
- Network access to Xellent SQL Server from WSL2
- Supplier identity GLN
