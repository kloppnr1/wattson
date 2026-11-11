# WattsOn — Learnings for the next project

Purpose: carry over what WattsOn taught us into a new project whose only goal is the **smallest system that Energinet approves for EDI communication with DataHub 3 as an elleverandør (DDQ)**.

Each point is marked:
- **[code]** — taken directly from this repository's code.
- **[confirm]** — must be checked against Energinet's current documentation or our own preprod traffic before relying on it.

---

## 1. What to reuse as-is

### 1.1 Authentication [code]
`src/WattsOn.Infrastructure/DataHub/DataHubClient.cs`, `DataHubSettings.cs`

- OAuth2 client credentials against Azure AD: `POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token`.
- Preprod values used: tenant `20e7a6b4-86e0-4e7a-a34d-6dc5a75d1982`, scope `65877f1b-1aef-42b3-adc7-3009608f27a3/.default`. Production has different values [confirm].
- Cache the token, refresh 5 minutes before expiry, guard refresh with a lock.
- On 401/403: drop the cached token and retry later.

### 1.2 Transport [code]
- Base URL: `https://preprod.b2b.datahub3.dk/v1.0/cim` (prod: `https://b2b.datahub3.dk/v1.0/cim`).
- **Peek** (GET) three queues: `/processes`, `/measuredata`, `/aggregations`.
  - `204`/`404` = queue empty.
  - Message id is in the `MessageId` response header.
- **Dequeue**: `DELETE /dequeue/{MessageId}`.
- Drain a queue (peek until empty), then wait. Poll interval ≥ 60 s (DataHub recommendation noted in `DataHubSettings`).
- **Send** (POST) endpoint per outgoing RSM — `DataHubEndpoints.cs`:

  | RSM | Endpoint |
  |---|---|
  | RSM-001 | `/requestchangeofsupplier` |
  | RSM-005 | `/requestendofsupply` |
  | RSM-006 | `/requestaccountingpointcharacteristics` |
  | RSM-012 | `/notifyvalidatedmeasuredata` |
  | RSM-015 | `/requestvalidatedmeasurements` |
  | RSM-016 | `/requestaggregatedmeasuredata` |
  | RSM-017 | `/requestwholesalesettlement` |
  | RSM-020, RSM-027, RSM-032, RSM-035 | `/requestservice` |
  | RSM-021 | `/requestchangeaccountingpointcharacteristics` |

- Response handling that worked:
  - `200`/`202` → accepted.
  - `400` → permanent rejection, do not retry.
  - `401`/`403` → re-authenticate.
  - `503` and network errors → retry with backoff (planned maintenance windows return 503).

### 1.3 Reliability pattern [code]
- **Inbound: store first, then dequeue.** Persist the raw payload, commit, and only then call dequeue. Deduplicate on `MessageId`, because a failed dequeue means the same message comes back.
- **Outbound: outbox table.** Write the message in the same transaction as the business change. A worker sends it with exponential backoff (30 s × 2ⁿ, capped at 30 min, max 5 attempts) and records the DataHub response.
- **Keep the raw payload forever.** It is the audit trail, and it lets us replay messages after a parser fix.

### 1.4 CIM JSON shape [code]
`src/WattsOn.Domain/Messaging/CimDocumentBuilder.cs`

- The root has one key, `<Name>_MarketDocument`.
- Property names are **flat dotted strings**: `"process.processType"` is one key, not a nested object.
- Values are wrapped:
  - Codes: `{ "value": "E03" }`.
  - Identifiers: `{ "codingScheme": "A10", "value": "<GLN>" }`.
- Coding schemes:
  - `A10` = GLN.
  - `A10` is also used on the GSRN in our builder [confirm].
  - Customer CPR = `ARR`, CVR = `VA`.
- Header fields:
  - `mRID`, `type`, `process.processType`, `businessSector.type = 23`.
  - Sender/receiver `mRID` and `marketRole.type`.
  - `createdDateTime` in UTC, format `yyyy-MM-ddTHH:mm:ssZ`.
- Roles:
  - `DDQ` = elleverandør (us).
  - `DDZ` = DataHub as metering point administrator (receiver for masterdata processes).
  - `DGL` = metered data aggregator (receiver for data requests).
- DataHub GLN: `5790001330552`.
- Transactions are in `MktActivityRecord[]` (masterdata processes) or `Series[]` (metered data, prices, aggregations).
- Inbound replies:
  - `type = A01` is a confirmation, `A02` a rejection.
  - The reply points back to our transaction via `originalTransactionIDReference_MktActivityRecord.mRID`.

### 1.5 Message classification [code]
`src/WattsOn.Infrastructure/DataHub/CimMessageClassifier.cs`

- Classify first by the root document name (e.g. `ConfirmRequestChangeOfSupplier_MarketDocument`, `NotifyEndOfSupply_MarketDocument`, `NotifyValidatedMeasureData_MarketDocument`).
- Fall back to `process.processType`:
  - `E03` = change of supplier
  - `E65` = move-in
  - `E20` = end of supply
  - `E23` = periodic metered data
  - `D18` / `D08` / `D17` = charge masterdata / prices / charge links
- Classification must never throw. An unknown message is stored and flagged, not dropped.

### 1.6 Domain rules worth keeping [code]
- GLN and GSRN as value objects with check-digit validation (`src/WattsOn.Domain/ValueObjects`).
- An explicit state machine per BRS process, with logged transitions (`src/WattsOn.Domain/Processes`). This doubles as test evidence.
- Pure, static domain handlers: no I/O, easy to unit test.
- Time series are versioned, never overwritten. A correction from DataHub creates a new version.
- All timestamps in UTC. Danish day boundaries are 22:00Z (summer) and 23:00Z (winter). On Windows the timezone id is `Romance Standard Time`, not `Europe/Copenhagen` (fix `af6c425`).

---

## 2. Mistakes not to repeat

### 2.1 Scope [code]
WattsOn grew to:
- 23 BRS inbox handlers;
- a 13-page React UI (~7k lines);
- a Xellent migration tool;
- Peppol-style settlement documents;
- a spot price worker;
- recalculation/comparison UI.

That is about 17k lines of C# outside migrations. None of it beyond the DataHub messaging is needed for approval. Approval tests the EDI exchange, not settlement quality.

### 2.2 The most important outgoing process was never wired [code]
- Starting a supplier change (sending RSM-001 / E03 to DataHub) exists only inside `SimulationEndpoints.cs`. That path fakes DataHub's confirmation and never writes an outbox message.
- `Brs001Handler.InitiateSupplierChange` creates the process but no CIM document.
- Real sends exist only for:
  - RSM-005 (BRS-002 end of supply, BRS-010 move-out, BRS-011);
  - RSM-001 with D07 (BRS-003);
  - RSM-027 and the request-type messages.
- If preprod supplier changes were done, they were done outside this code path [confirm].

### 2.3 Replies cannot be correlated by transaction id [code]
- `CimDocumentBuilder.AddTransaction` generates a new GUID as the transaction `mRID`.
- The handlers store a *different* id on the process (e.g. `BRS002-yyyyMMddHHmmss-xxxx`).
- The id we send is therefore never stored. Inbound handlers fall back to "latest open process for this GSRN" (`Brs001InboxHandler`, `Brs002InboxHandler`). This breaks as soon as there are two open processes on one metering point.
- **Next time:** generate the transaction `mRID` once, store it on the process, and match replies on `originalTransactionIDReference_MktActivityRecord.mRID`.

### 2.4 The parser only reads the first item [code]
`src/WattsOn.Worker/Routing/CimPayloadExtractor.cs` takes only:
- `MktActivityRecord[0]`;
- `Series[0]`;
- `Period[0]`.

DataHub bundles several transactions per document (e.g. many series in one RSM-012, many charges in a D08). Everything after the first item is silently ignored.

**Next time:** iterate every transaction and every period. Parse into typed records per RSM, not into a flattened dictionary with aliased keys (today `effectiveDate`/`linkStart` and `stopDate`/`linkEnd` are copies of the same field).

### 2.5 Errors that look like "nothing to do" [code]
- `PeekAsync` returns `null` on HTTP errors and non-success status codes, so the fetcher treats an outage as an empty queue.
- A dequeue without a `MessageId` header falls back to the document `mRID`, which is probably not the queue message id [confirm].
- The router marks messages with an unknown BRS as **processed**.
- Inbox messages stop retrying after 5 attempts, with no alert.

**Next time:** separate "empty" from "failed", put unknown or failed messages in a visible dead-letter state, and alert on it.

### 2.6 Unverified codes [code]
- `RsmDocumentConfig` marks RSM-032 and RSM-035 as "pending verification". Both use `E0G` as document type, which looks like a business reason, not a document type.
- Before building any message, take its type code, process type and roles from the RSM documentation, and from a real preprod reply where possible.

### 2.7 Resolution [code + confirm]
- All simulation data is `PT1H`. The settlement detail endpoint aggregates `PT15M` to hourly as a workaround.
- Energinet's elleverandør test cases include RSM-012 at both 15- and 60-minute resolution.
- **Next time:** treat `PT15M` as the default from day one.

### 2.8 The simulation is not evidence [code]
- Simulation endpoints build their own "incoming" envelopes and call domain handlers directly, bypassing peek → inbox → classify → route.
- They prove the domain logic, not the integration, and they drifted from the real formats.
- **Next time:** save real preprod messages (anonymised) as test fixtures and replay them through the real inbound pipeline.

### 2.9 Docs drifted from code [code]
- `docs/datahub/*.md` describe a `Draft` state that does not exist in the state machines.
- The README lists BRS-034 as planned while handlers for it exist.
- **Next time:** keep one source of truth per process — the test cases.

---

## 3. What "approved" requires

From Energinet's public material (fetched 2026-11; re-check before planning):
- **Forskrift F1:** an actor's IT system must pass a **system test** and then an **actor test** before it may exchange messages with DataHub.
- The elleverandør terms say the same for suppliers using EDI.
- The DataHub 3 approval page for phase 2:
  - An actor using its own IT system runs the test cases for its own actor. An IT vendor must test at least 2 actors per market role/protocol.
  - When the test cases are done, a case is submitted to DataHub Support (`datahub@energinet.dk`).
- The elleverandør test case catalogue:
  - Run the test cases that match the messages you use, or expect to use, in production.
  - The published cases cover receiving RSM-012 (E23) across metering point types and 15/60-minute resolution.
  - Some cases depend on BRS-024/BRS-025 being in use.

**[confirm]** The exact mandatory test case list was not retrievable from this session: `energinet.atlassian.net` is blocked by the network policy. Get the list from the "Test Cases – Elleverandør" Confluence page, or from DataHub Support, **before** writing code. That list is the scope of the new project.

---

## 4. Proposed minimal scope for the new project

Assumption: a supplier that gains, keeps and loses customers, and receives the data it needs to bill them. Cut or add processes once the test case list in §3 is confirmed.

| Area | Must have | WattsOn reference |
|---|---|---|
| Transport | OAuth2, peek/dequeue on 3 queues, POST, inbox/outbox, dedup, dead-letter | `DataHubClient`, `DataHubInboxFetcher`, `OutboxDispatchWorker` |
| BRS-001 change of supplier | Send RSM-001 (E03), handle confirm/reject, receive masterdata; handle RSM-004 when losing a customer | `Brs001Handler` — the send part must be built |
| BRS-009 move-in | Send RSM-001 (E65), handle confirm/reject | `Brs009Handler` |
| BRS-002 end of supply / BRS-010 move-out | Send RSM-005, handle confirm/reject | `Brs002Handler`, `Brs010Handler` |
| Masterdata received | Metering point masterdata after a gain (BRS-004/006 messages) | `Brs004/006InboxHandler` |
| Metered data | Receive RSM-012 (E23), all transactions, PT15M + PT1H, versioned | `Brs021InboxHandler` (fix §2.4) |
| Charges | Receive D18/D08/D17 (BRS-031/037) | `Brs031Handler` |
| Ops | Log of every sent/received document with raw payload; a way to resend and replay | `InboxMessage`, `OutboxMessage` |

**Out of scope until approved:**
- settlement engine, spot prices, margins;
- frontend beyond a message log;
- migration tooling, Peppol documents;
- every request-type process (BRS-023/024/025/027/034/038/039) — unless the test list requires one.

### Design rules for the new codebase
1. One typed model per RSM that we send or receive. Write a serializer test against a real preprod sample for each.
2. Transaction `mRID` generated once, stored, and used for correlation.
3. Parse every transaction in a document.
4. No simulation endpoints. Use real preprod fixtures replayed through the real pipeline.
5. Every test case in Energinet's list maps to one automated test and one preprod run, with its message ids recorded as evidence for the approval case.
6. Fail loudly: dead-letter plus an alert, never "marked processed".

---

## 5. Sources
- [Forskrift F1: EDI-kommunikation med DataHub i elmarkedet](https://www.energinet.dk/media/2a2jksg2/18-03427-12-forskrift-f1_energinet-hoering.pdf)
- [Vilkår for adgang til og brug af DataHub – Elleverandør](https://www.energinet.dk/media/bzzkzwh5/vilkaar-for-adgang-til-og-brug-af-datahub-elleverandoer-2021.pdf)
- [Godkendelse som It-leverandør eller aktør til DataHub 3 – Fase 2](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1228570770/Godkendelse+som+It-leverand+r+eller+akt+r+til+DataHub+3+-+Fase+2)
- [Test Cases – Elleverandør](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1228570791/Test+Cases+-+Elleverand+r)
- [DAB referat december 2023 (actor test made mandatory)](https://www.energinet.dk/media/2bej5bag/referat-dab-december-2023.pdf)
