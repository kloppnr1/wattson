# DataHub BRS Process Documentation

WattsOn integrates with Energinet's DataHub 3.0 via CIM JSON over REST. The following BRS (Business Required Specification) processes are supported:

| Process | Name | Status |
|---------|------|--------|
| [BRS-001](BRS-001.md) | Supplier Change (Leverandørskift) | Active |
| [BRS-009](BRS-009.md) | Move-In / Move-Out (Tilflytning / Fraflytning) | Active |
| [BRS-031](BRS-031.md) | Price / Charge Updates (Prissending) | Active |
| [BRS-034](BRS-034.md) | Request for Prices (Prisanmodning) | Planned |

## Authentication

All DataHub communication uses OAuth2 client credentials flow. WattsOn requests a Bearer token from DataHub's token endpoint and includes it in all API calls.

## Message Pattern

DataHub uses a post/peek/dequeue inbox-outbox pattern:

1. **Post** — WattsOn sends an outbound message to DataHub
2. **Peek** — WattsOn polls for inbound messages from DataHub
3. **Dequeue** — WattsOn acknowledges a processed message

WattsOn's `Worker` service handles this loop via the inbox/outbox pattern (`InboxMessage` / `OutboxMessage` domain entities).
