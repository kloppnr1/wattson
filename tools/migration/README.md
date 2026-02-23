# WattsOn Migration Tool

Two-phase CLI tool for migrating customer data from Xellent (AXDB50) to WattsOn.

**Phase 1 — Extract** from Xellent SQL Server (requires VPN/network access).
**Phase 2 — Push** cached JSON to WattsOn API (no VPN needed).

## Quick Start

```powershell
# Phase 1: Extract from Xellent (run from Windows — requires Trusted_Connection)
dotnet migration.dll extract `
    --accounts 405013 `
    --xellent-connection "Server=10.200.32.32;Database=AXDB50;Trusted_Connection=True;TrustServerCertificate=True" `
    --cache ./cache/extracted.json

# Phase 2: Push to WattsOn API
dotnet migration.dll push `
    --cache ./cache/extracted.json `
    --wattson-url http://localhost:5100
```

Supplier GLN and name are auto-resolved from the `--data-area-id` / `--company-id` combination (see [Supplier Mapping](#supplier-mapping)). Override with `--supplier-gln` / `--supplier-name` if needed.

## Commands

### `extract` — Extract from Xellent

Connects to AXDB50, extracts customer data, and saves to a local JSON cache file.

| Option | Description | Default |
|--------|-------------|---------|
| `--accounts` | Xellent account numbers (required, multiple) | — |
| `--xellent-connection` | SQL Server connection string (required) | — |
| `--company-id` | Xellent COMPANYID(s) — see [Supplier Mapping](#supplier-mapping) | `for` |
| `--data-area-id` | Xellent DATAAREAID — legal entity partition | `hol` |
| `--supplier-gln` | Override auto-resolved supplier GLN | auto |
| `--supplier-name` | Override auto-resolved supplier name | auto |
| `--include-timeseries` | Include hourly consumption data | `true` |
| `--since` | Time series cutoff date | 2 years ago |
| `--include-settlements` | Include settlement provenance | `true` |
| `--cache` | Output cache file path | `./cache/extracted.json` |

Extracts: customers, metering points, supplies, product periods, supplier margins (4-tier rate fallback), prices, price links, time series, and settlements.

### `push` — Push to WattsOn API

Reads cached JSON and pushes to WattsOn in order.

| Option | Description | Default |
|--------|-------------|---------|
| `--cache` | Path to extracted cache file | `./cache/extracted.json` |
| `--wattson-url` | WattsOn API base URL | `http://localhost:5100` |
| `--years-back` | Migration window: only push data from N years before supply end | `3` |

Push order:
1. Supplier identity (GLN + name)
2. Supplier products
3. Customers + metering points + supplies
4. Supply product periods
5. Supplier margin rates
6. Prices
7. Price links
8. Time series observations
9. Settlements with tariff + hourly detail lines

### `audit` — Data quality checks

Runs diagnostic checks against Xellent. Reads connection string from `appsettings.json`.

| Option | Description | Default |
|--------|-------------|---------|
| `--accounts` | Xellent account numbers (required) | — |
| `--company-id` | Xellent COMPANYID(s) | `for` |
| `--data-area-id` | Xellent DATAAREAID | `hol` |
| `--output` | Output directory for JSON report | `./cache/audit` |

Checks:
- **Rate columns** — confirms `RATE` = `ACCOUNTRATE` in `EXU_RATETABLE`
- **Rate accuracy** — compares extracted margin rates against billed amounts in `FlexBillingHistoryLine`
- **Product coverage** — verifies `EXU_PRODUCTTABLE` vs the extraction chain

### `report` — Generate HTML report from cache

| Option | Description | Default |
|--------|-------------|---------|
| `--cache` | Path to extracted cache file | `./cache/extracted.json` |
| `--output` | Output directory | `./cache/reports` |

### `xellent-report` — Settlement provenance report

Connects directly to Xellent and generates a reference settlement report using CorrectionService-equivalent logic.

| Option | Description | Default |
|--------|-------------|---------|
| `--accounts` | Xellent account numbers (required) | — |
| `--xellent-connection` | SQL Server connection string (required) | — |
| `--company-id` | Xellent COMPANYID(s) | `for` |
| `--data-area-id` | Xellent DATAAREAID | `hol` |
| `--output` | Output directory | `./cache/reports` |

### `schema` — Dump Xellent table columns

| Option | Description | Default |
|--------|-------------|---------|
| `--xellent-connection` | SQL Server connection string (required) | — |

## Supplier Mapping

The tool auto-resolves supplier GLN from the `(DataAreaId, CompanyId)` combination:

| DataAreaId | CompanyId | GLN | Supplier |
|------------|-----------|-----|----------|
| `hol` | `for` | 5790001103033 | Verdo Go Green |
| `hol` | `meh` | 5790001103040 | Midtjysk Elhandel |
| `Han` | `hni` | 5790002388309 | Aars Nibe Handel |
| `Han` | `vhe` | 5790002388309 | Aars Nibe Handel |
| `HJH` | `HJH` | 5790002529283 | Hjerting Handel |

Multiple company IDs under the same data area map to the same GLN (e.g., `hni` and `vhe` both resolve to Aars Nibe Handel):

```powershell
# Aars Nibe Handel (both hni and vhe company IDs)
dotnet migration.dll extract --accounts 123456 --data-area-id Han --company-id hni vhe ...
```

## Architecture

```
WattsOn.Migration.Cli          — CLI orchestrator (System.CommandLine)
WattsOn.Migration.Core         — Shared models (ExtractedCustomer, ExtractedData, etc.)
WattsOn.Migration.XellentData  — Xellent DB access (EF Core + SQL Server)
WattsOn.Migration.WattsOnApi   — HTTP client for WattsOn /api/migration/* endpoints
```

## Prerequisites

- **Extract/audit**: Network access to Xellent SQL Server (Windows Auth — run from Windows or domain-joined machine)
- **Push**: WattsOn API running with migration endpoints
- **Audit**: `appsettings.json` in the CLI output directory with `ConnectionStrings:Xellent`
