# ⚡ WattsOn

[![CI](https://github.com/kloppnr1/wattson/actions/workflows/ci.yml/badge.svg)](https://github.com/kloppnr1/wattson/actions/workflows/ci.yml)

Settlement engine for the Danish electricity market (DataHub 3.0).

WattsOn handles **forbrugsafregning** (consumption settlement) for an electricity supplier (elleverandør). It processes metering data, calculates settlements with full price breakdowns, detects corrections when DataHub data changes, and exposes everything via API for an external invoicing system.

> **WattsOn is not an invoicing system.** It's the settlement engine that feeds one. The external system pulls settlements via API, invoices the customer, and confirms back. WattsOn's core value: detect DataHub changes that affect already-invoiced settlements and create adjustment (delta) settlements automatically.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        WattsOn                              │
│                                                             │
│  ┌──────────┐  ┌──────────────┐  ┌───────────────────────┐  │
│  │ Frontend │  │   REST API   │  │       Workers         │  │
│  │ (React)  │←→│  (ASP.NET)   │  │                       │  │
│  │ :5173    │  │   :5100      │  │  InboxPollingWorker   │  │
│  └──────────┘  └──────┬───────┘  │  SettlementWorker     │  │
│                       │          │  SpotPriceWorker       │  │
│                       ▼          │  OutboxDispatchWorker  │  │
│              ┌────────────────┐  └───────────┬───────────┘  │
│              │  Domain Layer  │              │              │
│              │  (Pure C#)     │              │              │
│              └────────┬───────┘              │              │
│                       │                      │              │
│              ┌────────▼──────────────────────▼──┐           │
│              │  PostgreSQL + TimescaleDB         │           │
│              │  :5432                            │           │
│              └──────────────────────────────────┘           │
└─────────────────────────────────────────────────────────────┘
         ▲                                    ▲
         │ REST API                           │ CIM JSON
         │                                    │ (Peek/Dequeue)
         ▼                                    ▼
  External Invoicing                     Energinet DataHub 3.0
     System                              (via CIM Webservice)
```

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **Domain** | .NET 9 / C#, Clean Architecture + DDD |
| **Database** | PostgreSQL 16 + TimescaleDB |
| **API** | ASP.NET Core Minimal API |
| **Frontend** | React 19 + TypeScript + Vite + Ant Design 6 |
| **Testing** | xUnit, Testcontainers, Playwright |
| **Protocol** | CIM JSON (DataHub 3.0) |

## Quick Start

```bash
docker compose up
```

That's it. The system starts with:

| Service | URL |
|---------|-----|
| **Frontend** | http://localhost:5173 |
| **API** | http://localhost:5100 |
| **Database** | localhost:5432 |

The API auto-migrates the database and seeds a default supplier identity on startup.

**No seed data.** The system proves itself by processing messages. Use the Simulation page to trigger BRS processes and watch settlements appear.

## Domain Model

### Core Entities

| Entity | Description |
|--------|-------------|
| **Customer** | End customer (person with CPR or company with CVR), belongs to a supplier identity (GLN) |
| **MeteringPoint** | Physical or virtual metering point, identified by 18-digit GSRN |
| **Supply** | Links a customer to a metering point for a period |
| **TimeSeries** | Versioned hourly consumption data for a metering point |
| **Settlement** | Calculated settlement with line items per charge, lifecycle: Calculated → Invoiced → Adjusted |
| **Price** | A charge (tariff, subscription, or fee) with time-varying price points |
| **PriceLink** | Links a price to a metering point for a period |
| **SpotPrice** | Hourly Nord Pool spot prices per price area (DK1/DK2) |
| **SupplierIdentity** | The supplier's GLN identity (supports multi-GLN) |

### BRS Processes

| Process | Name | Status |
|---------|------|--------|
| **BRS-001** | Leverandørskift (Change of Supplier) | ✅ Initiator + Recipient |
| **BRS-009** | Tilflytning / Fraflytning (Move-In / Move-Out) | ✅ Both directions |
| **BRS-031** | Opdatering af priser (Price Updates) | ✅ D08, D18, D17 |
| **BRS-034** | Anmodning om priser (Request Prices) | 📋 Planned |

### Settlement Flow

```
TimeSeries received
       │
       ▼
SettlementWorker detects unsettled data
       │
       ▼
Resolves active PriceLinks for MeteringPoint + Period
       │
       ▼
SettlementCalculator produces Settlement with lines:
  ├── Nettarif C (grid tariff)    → 234.56 DKK
  ├── Systemtarif                 →  45.12 DKK
  ├── Transmissionstarif          →  23.45 DKK
  ├── Elafgift (electricity tax)  →  89.10 DKK
  └── Supplier margin             →  67.80 DKK
                            Total:  460.03 DKK
       │
       ▼
External invoicing system:
  GET /api/settlements/uninvoiced    → pick up
  POST /api/settlements/{id}/mark-invoiced → confirm
       │
       ▼
If DataHub sends corrected TimeSeries later:
  → SettlementWorker auto-creates delta correction
  → GET /api/settlements/adjustments → credit/debit note
```

## API Endpoints

### Settlements (for external invoicing system)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/settlements/uninvoiced` | New settlements ready for invoicing |
| `GET` | `/api/settlements/adjustments` | Correction settlements (credit/debit) |
| `POST` | `/api/settlements/{id}/mark-invoiced` | Confirm settlement was invoiced |
| `GET` | `/api/settlement-documents` | Peppol-aligned pre-invoice documents |
| `GET` | `/api/settlement-documents/{id}` | Single document with full breakdown |
| `POST` | `/api/settlement-documents/{id}/confirm` | Confirm document invoiced |

### Prices (supplier margin + DataHub tariffs)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/prices` | All prices/charges |
| `POST` | `/api/prices` | Create price (supplier margin) |
| `GET` | `/api/price-links?meteringPointId={id}` | Price links for a metering point |
| `POST` | `/api/price-links` | Link a price to a metering point |

### Master Data

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/customers` | List customers |
| `GET` | `/api/customers/{id}` | Customer detail with supplies |
| `POST` | `/api/customers` | Create customer |
| `GET` | `/api/metering-points` | List metering points |
| `GET` | `/api/metering-points/{id}` | Metering point detail |
| `GET` | `/api/supplies` | List supplies |
| `GET` | `/api/spot-prices?area=DK1&days=7` | Spot prices |

### DataHub Communication

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/inbox` | Inbox messages from DataHub |
| `GET` | `/api/outbox` | Outbox messages to DataHub |
| `GET` | `/api/processes` | BRS process overview |

### Simulation

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/simulation/supplier-change` | Simulate BRS-001 with full flow |
| `POST` | `/api/simulation/supplier-change-outgoing` | Simulate losing a customer |
| `POST` | `/api/simulation/move-in` | Simulate BRS-009 move-in |

## Frontend Pages

| Page | Description |
|------|-------------|
| **Dashboard** | Overview and KPIs |
| **Customers** | Customer list and detail view (hub for everything about a customer) |
| **Metering Points** | GSRN list with drill-down |
| **Supplies** | Active and historical supply relationships |
| **Settlements** | Settlement list with detail breakdown |
| **Processes** | BRS process overview with state transitions |
| **Inbox / Outbox** | DataHub message monitoring |
| **Simulation** | Trigger BRS processes and watch the engine work |
| **Admin** | Supplier identity management |

## Project Structure

```
src/
├── WattsOn.Domain/           # Pure domain — entities, value objects, services
│   ├── Common/               # Entity, ValueObject, DomainEvent base classes
│   ├── Entities/             # Customer, MeteringPoint, Settlement, Price, ...
│   ├── Enums/                # PriceType, SettlementStatus, ProcessType, ...
│   ├── Messaging/            # InboxMessage, OutboxMessage
│   ├── Processes/            # BrsProcess + state machines
│   ├── Services/             # Brs001Handler, Brs009Handler, Brs031Handler, SettlementCalculator
│   └── ValueObjects/         # GlnNumber, Gsrn, CprNumber, Money, Period, ...
├── WattsOn.Application/      # Interfaces (IWattsOnDbContext)
├── WattsOn.Infrastructure/   # EF Core, persistence, migrations
├── WattsOn.Api/              # Minimal API endpoints
├── WattsOn.Worker/           # Background workers
│   ├── InboxPollingWorker    # Routes BRS messages to handlers
│   ├── SettlementWorker      # Auto-settles new time series
│   ├── SpotPriceWorker       # Polls Energi Data Service for spot prices
│   └── OutboxDispatchWorker  # Sends messages to DataHub
└── WattsOn.Frontend/         # React + TypeScript + Ant Design
    └── src/pages/            # 13 pages

tests/
├── WattsOn.Domain.Tests/     # 153 unit tests (pure domain logic)
└── WattsOn.Infrastructure.Tests/  # Integration tests (Testcontainers)
```

## Testing

```bash
# Domain tests (fast, no dependencies)
dotnet test tests/WattsOn.Domain.Tests

# Integration tests (requires Docker for Testcontainers)
dotnet test tests/WattsOn.Infrastructure.Tests
```

## Key Design Decisions

- **No invoicing** — WattsOn is the settlement engine only
- **No seed data** — system proves itself by processing real messages
- **Pragmatic monolith** — no microservices, no event sourcing
- **Difference-based corrections** — delta settlements, not full credit+reissue
- **Customer owns the GLN** — SupplierIdentity is on Customer, not Supply
- **Time series versioned** — never overwrite, always new version
- **Inbox/outbox pattern** — reliable DataHub message processing
- **Explicit state machines** — every BRS process has a state machine
- **Pure domain services** — handlers are static, no dependencies, just math and rules
- **Peppol-aligned documents** — settlement documents follow BIS Billing 3.0 structure

## DataHub 3.0 Integration

WattsOn communicates with Energinet's DataHub 3.0 via CIM JSON over REST:

- **Authentication:** OAuth2 client credentials → Bearer token
- **Message pattern:** Post (send) → Peek (receive) → Dequeue (acknowledge)
- **Environments:** Preprod (`preprod.b2b.datahub3.dk`) / Prod (`b2b.datahub3.dk`)

Documentation for all supported BRS processes is in [`docs/datahub/`](docs/datahub/).

## License

Proprietary — see [LICENSE](LICENSE). All rights reserved.
