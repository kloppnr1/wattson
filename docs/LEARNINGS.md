# WattsOn → DataHub communication platform: learnings

**New project:** built from scratch. It is a base communication platform for DataHub 3, approved by Energinet.
**WattsOn's role:** a reference only. No code is carried over. What carries over is knowledge: what DataHub expects, what worked in preprod, and what went wrong.

Each point is marked:
- **[code]** — taken directly from this repository.
- **[confirm]** — must be checked against Energinet's current documentation or our own preprod traffic.

---

## 1. Platform vision in one paragraph

The platform does three things:
1. It owns everything between our systems and DataHub: authentication, sending, fetching, acknowledging, message formats, validation, correlation of replies, process state, and the audit log.
2. Business systems (settlement, CRM, customer portal) talk to the platform through a stable internal API and events. They never touch CIM JSON.
3. It is the thing Energinet tests and approves. Business systems can change later without a new approval.

Rule of thumb: **if Energinet cannot observe it on the wire, it does not belong in the platform.** Prices, settlement, invoicing and customers are business-system concerns.

---

## 2. Decisions to make before writing code

| Decision | Why it matters |
|---|---|
| **Which market role(s) first** | Approval is per market role and protocol. Start with one: elleverandør (DDQ) is what we know from WattsOn. Design so that other roles can be added. |
| **Own actor or IT vendor** | An actor using its own IT system runs the test cases for itself. An IT vendor must test **at least 2 actors per market role/protocol**. If the platform will serve other companies, plan for two test actors from the start. |
| **Single or multi-GLN** | WattsOn already needed several supplier GLNs (HJH, HAN, MEH, Verdo Go Green, commit `2251746`) [code]. Every message, credential and queue must be scoped to an actor from day one. |
| **Credentials per actor** | Each actor has its own B2B access (client id/secret, or certificate) [confirm]. The platform needs a secret store and a token cache per actor. |
| **The exact test case list** | This is the scope of v1. Get it from the "Test Cases" Confluence pages, or from DataHub Support, before planning. It could not be fetched from this session: `energinet.atlassian.net` is blocked by the network policy. |

---

## 3. Knowledge to carry over

### 3.1 Authentication [code]
Reference: `src/WattsOn.Infrastructure/DataHub/DataHubClient.cs`

- OAuth2 client credentials against Azure AD: `POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token`.
- Preprod values: tenant `20e7a6b4-86e0-4e7a-a34d-6dc5a75d1982`, scope `65877f1b-1aef-42b3-adc7-3009608f27a3/.default`. Production values differ [confirm].
- Cache the token, refresh 5 minutes before expiry, guard refresh with a lock.
- On 401/403: drop the cached token and re-authenticate.

### 3.2 Transport [code]
- Base URL: `https://preprod.b2b.datahub3.dk/v1.0/cim` (prod: `https://b2b.datahub3.dk/v1.0/cim`).
- **Peek** (GET) three queues: `/processes`, `/measuredata`, `/aggregations`.
  - `204`/`404` = queue empty.
  - The message id is in the `MessageId` response header.
- **Dequeue**: `DELETE /dequeue/{MessageId}`.
- Drain each queue (peek until empty), then wait. Poll interval ≥ 60 s.
- **Send**: one POST endpoint per outgoing RSM (`DataHubEndpoints.cs`):

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

- HTTP outcomes:
  - `200`/`202` → accepted.
  - `400` → permanent rejection, do not retry.
  - `401`/`403` → re-authenticate.
  - `503` and network errors → retry with backoff (planned maintenance windows return 503).

### 3.3 Reliability [code]
- **Inbound: store first, then dequeue.** Commit the raw payload, then dequeue. Deduplicate on `MessageId`, because a failed dequeue means the message comes back.
- **Outbound: outbox.** Write the message in the same transaction as the request that caused it. A worker sends it with exponential backoff (30 s × 2ⁿ, capped at 30 min).
- **Keep every raw document, in and out, forever.** It is the audit trail, the evidence for the approval case, and the input for replay after a parser fix.

### 3.4 CIM JSON format [code]
Reference: `src/WattsOn.Domain/Messaging/CimDocumentBuilder.cs`

- The root has one key, `<Name>_MarketDocument`.
- Property names are **flat dotted strings**: `"process.processType"` is a single key.
- Values are wrapped:
  - Codes: `{ "value": "E03" }`.
  - Identifiers: `{ "codingScheme": "A10", "value": "<GLN>" }`.
- Coding schemes:
  - `A10` = GLN.
  - `A10` is also used on the GSRN in our builder [confirm].
  - CPR = `ARR`, CVR = `VA`.
- Header fields:
  - `mRID`, `type`, `process.processType`, `businessSector.type = 23`.
  - Sender/receiver `mRID` and `marketRole.type`.
  - `createdDateTime` in UTC, format `yyyy-MM-ddTHH:mm:ssZ`.
- Roles: `DDQ` = elleverandør, `DDZ` = DataHub as metering point administrator, `DGL` = metered data aggregator. DataHub GLN: `5790001330552`.
- Transactions are in `MktActivityRecord[]` (masterdata processes) or `Series[]` (metered data, prices, aggregations).
- Replies: `type A01` = confirm, `A02` = reject. The reply references our transaction through `originalTransactionIDReference_MktActivityRecord.mRID`.
- Classify inbound documents by root document name first, then by `process.processType`:
  - `E03` = change of supplier
  - `E65` = move-in
  - `E20` = end of supply
  - `E23` = periodic metered data
  - `D18` / `D08` / `D17` = charge masterdata / prices / charge links

  (`CimMessageClassifier.cs`)

### 3.5 Time [code]
- Everything is in UTC on the wire.
- Danish day boundaries are 22:00Z (summer) and 23:00Z (winter).
- On Windows the timezone id is `Romance Standard Time`, not `Europe/Copenhagen` (fix `af6c425`).
- Expect both `PT15M` and `PT1H` resolution. Energinet's test cases cover both.

### 3.6 Useful external sources [confirm]
- DataHub 3 itself is developed openly by Energinet on GitHub (`Energinet-DataHub` organisation). Its EDI/B2B subsystem should hold the authoritative message definitions and examples.
- If that holds, generate our message models and validation from those definitions instead of hand-writing them. Hand-writing is how WattsOn ended up with unverified codes (§4.5).

---

## 4. Mistakes not to repeat

### 4.1 Building the business before the platform
WattsOn grew to:
- 23 process handlers;
- a 13-page UI;
- a settlement engine;
- spot prices;
- Xellent migration;
- Peppol documents.

That is about 17k lines of C#, most of it irrelevant to approval. **The new platform contains no business logic.**

### 4.2 The key outgoing process was never really sent [code]
- Starting a supplier change (RSM-001 / E03) existed only in a simulation endpoint, which faked DataHub's reply. No outbox message was ever written.
- **Rule:** every outgoing message type has exactly one code path, the real one.

### 4.3 Replies could not be correlated [code]
- The transaction `mRID` was generated inside the document builder and never stored.
- Replies were matched on "latest open process for this GSRN". That breaks as soon as one metering point has two open processes.
- **Rule:** generate the transaction id once, store it, and correlate on `originalTransactionIDReference`.

### 4.4 The parser read only the first item [code]
- `CimPayloadExtractor` took only `MktActivityRecord[0]`, `Series[0]` and `Period[0]`. Bundled documents silently lost data.
- It flattened everything into a loosely typed dictionary with duplicated keys.
- **Rule:** one typed model per RSM, and iterate every transaction and every period.

### 4.5 Guessed codes [code]
- RSM-032 and RSM-035 were "pending verification", with `E0G` used as document type.
- **Rule:** no message type goes in without a spec reference, plus a real preprod sample as a test fixture.

### 4.6 Silent failures [code]
- A peek error was treated as an empty queue.
- Unknown messages were marked processed.
- Messages that failed 5 times stopped with no alert.
- **Rule:** explicit states (received / parsed / handled / failed / dead-lettered), visible in the ops view, with alerts.

### 4.7 The simulation replaced the integration [code]
- Simulation endpoints built fake inbound envelopes and called handlers directly. Formats drifted from reality.
- **Rule:** tests replay recorded preprod documents through the real pipeline. A DataHub stub, if needed, speaks the real HTTP API.

---

## 5. Proposed architecture

```
 Business systems (settlement, CRM, portal …)
        │  Internal API: commands ("request supplier change")
        │  Events: "supplier change confirmed", "metered data received"
        ▼
┌────────────────────────── Platform ──────────────────────────┐
│ Process layer   one state machine per BRS, correlation by     │
│                 transaction mRID, deadlines, events out       │
│ Message layer   typed RSM models ⇄ CIM JSON, schema check,    │
│                 classification                                │
│ Transport layer per-actor auth, outbox sender, peek/dequeue   │
│                 fetcher, retries, dead-letter                 │
│ Store           raw documents (in/out), process state, audit  │
│ Ops view        message log, search by GSRN/transaction,      │
│                 resend, replay                                │
└───────────────────────────────────────────────────────────────┘
        │  CIM JSON over HTTPS (OAuth2)
        ▼
   Energinet DataHub 3 (preprod / prod)
```

Principles:
1. **Actor-scoped everything.** Every row carries the actor GLN and market role.
2. **Raw first.** Persist bytes before parsing. Parsing can fail and be retried; data cannot be lost.
3. **Idempotent.** The same inbound `MessageId` or outbound command id is processed once.
4. **Events out, not database sharing.** Business systems consume events and API calls only.
5. **Evidence by design.** Each approval test case links to its outbound/inbound message ids, so the approval case can be put together from the ops view.

---

## 6. Suggested first version

Scope = Energinet's elleverandør test cases [confirm the list]. Expected content, based on what a supplier sends and receives:

| Order | Item | Done when |
|---|---|---|
| 1 | Transport: per-actor auth, peek/dequeue on 3 queues, outbox, raw store, dead-letter | Real preprod documents are fetched, stored and dequeued; a send gets 202 |
| 2 | Message layer for every RSM in the test list, with preprod fixtures | Round-trip tests pass on recorded documents |
| 3 | BRS-001 change of supplier (send E03, confirm/reject, masterdata), and losing a customer (RSM-004) | Preprod run recorded |
| 4 | BRS-009 move-in, BRS-002 end of supply, BRS-010 move-out | Preprod runs recorded |
| 5 | Receive metered data RSM-012 (PT15M + PT1H, all series) and charges (D18/D08/D17) | Matches the test cases |
| 6 | Ops view and evidence export | The approval case can be submitted to DataHub Support |

Not in the first version: anything without a matching test case.

---

## 7. Sources
- [Forskrift F1: EDI-kommunikation med DataHub i elmarkedet](https://www.energinet.dk/media/2a2jksg2/18-03427-12-forskrift-f1_energinet-hoering.pdf) — system test plus actor test required
- [Vilkår for adgang til og brug af DataHub – Elleverandør](https://www.energinet.dk/media/bzzkzwh5/vilkaar-for-adgang-til-og-brug-af-datahub-elleverandoer-2021.pdf)
- [Godkendelse som It-leverandør eller aktør til DataHub 3 – Fase 2](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1228570770/Godkendelse+som+It-leverand+r+eller+akt+r+til+DataHub+3+-+Fase+2) — own actor vs. IT vendor (≥ 2 actors per role/protocol), submit to `datahub@energinet.dk`
- [Test Cases – Elleverandør](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1228570791/Test+Cases+-+Elleverand+r) — run the cases for the messages used in production
- [DAB referat december 2023](https://www.energinet.dk/media/2bej5bag/referat-dab-december-2023.pdf) — actor test made mandatory
