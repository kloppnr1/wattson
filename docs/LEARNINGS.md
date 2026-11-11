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

## 2a. Decisions made (2026-11-11)

| Decision | Choice | Consequence |
|---|---|---|
| Own actor or IT vendor | **IT vendor** — the platform serves other companies | Use the **IT-Lev** column of the test cases (including the `-IT` cases), and run them for **at least 2 actors** |
| Market role | **Elleverandør (DDQ)** | One role, one approval |
| Optional processes | **None** | Skip the cases marked "B" (BRS-005, 034, 038, 039) |
| Preprod access | **Already have it** | Still needed: users for 2 test actors (e.g. Energinet's Ellev_1A / Ellev_1B), each with its own client id/secret |

The platform is **multi-actor from day one**: every credential, queue poll, message and process is scoped to an actor GLN.

## 2. Decisions to make before writing code (background)

| Decision | Why it matters |
|---|---|
| **Which market role(s) first** | Approval is per market role and protocol. Start with one: elleverandør (DDQ) is what we know from WattsOn. Design so that other roles can be added. |
| **Own actor or IT vendor** | An actor using its own IT system runs the test cases for itself. An IT vendor must test **at least 2 actors per market role/protocol**. If the platform will serve other companies, plan for two test actors from the start. |
| **Single or multi-GLN** | WattsOn already needed several supplier GLNs (HJH, HAN, MEH, Verdo Go Green, commit `2251746`) [code]. Every message, credential and queue must be scoped to an actor from day one. |
| **Credentials per actor** | Each market role per actor has its own Client ID + Client Secret, created in the DataHub 3 web portal under *Aktører → B2B adgang*. No certificates. **A secret is valid for 12 months**, so the platform needs a secret store, a token cache per actor and an expiry alert. |
| **The exact test case list** | Now known — see §6.1 (phase 3 elleverandør test cases, page updated 3 Sep 2026). |

---

## 3. Knowledge to carry over

### 3.1 Authentication [code]
Reference: `src/WattsOn.Infrastructure/DataHub/DataHubClient.cs`

- OAuth2 client credentials against Azure AD: `POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token`.
- Preprod: tenant `20e7a6b4-86e0-4e7a-a34d-6dc5a75d1982`, scope `65877f1b-1aef-42b3-adc7-3009608f27a3/.default`.
- Production (Confluence): tenant `4b8c3f88-6cca-480c-af02-b2d2f220913f`, scope `336add08-dc41-44ba-abaa-bbd856c01390/.default`.
- The token lasts 1 hour. Cache it, refresh 5 minutes before expiry, guard refresh with a lock.
- On 401/403: drop the cached token and re-authenticate.

### 3.2 Transport [code]
*Confirmed against Confluence. Note: Energinet removed the URL columns from the current endpoint page on 12 Oct 2026 (v73) and now points to the ServiceNow support portal. The URLs below come from v72; HTTP methods come from 2024 page versions. Re-confirm with DataHub Support.*

- Base URL: `https://preprod.b2b.datahub3.dk/v1.0/cim/`. Production: `https://b2b.datahub3.dk/v1.0/cim/` (older pages say `prod.b2b.datahub3.dk`).
- **Peek** (GET) three queues. The `Content-Type` (`application/json` or `application/xml`) selects the format.

  | Queue | Carries |
  |---|---|
  | `/processes` | masterdata: RSM-001/003/004/005/006/020/021/022/024/025/027/028/030/031/032/033/034/035 |
  | `/measuredata` | RSM-009, RSM-012, RSM-015 reject, RSM-018 |
  | `/aggregations` | RSM-014, RSM-016 reject, RSM-017 reject, RSM-019 |

  Responses: `200` message, `204` empty, `415` wrong content type, `503` maintenance (retry in 5 min).
- **Dequeue**: `DELETE /dequeue/{message-id}`. The message id is "in the peek response". The docs don't name a header; WattsOn read a `MessageId` header [confirm on preprod]. Responses: `200` removed, `400` could not be acknowledged, `503` maintenance.
- **Poll limit**: each queue may be peeked at most once a minute. While messages are available, keep peeking until the queue is empty, then wait for the next run.
- **Send** (POST):

  | RSM | Endpoint |
  |---|---|
  | RSM-001 | `/requestchangeofsupplier` |
  | RSM-003 confirm / reject (sent by the supplier) | `/confirmrequestreallocatechangeofsupplier`, `/rejectrequestreallocatechangeofsupplier` |
  | RSM-005 | `/requestendofsupply` |
  | RSM-006 | `/requestaccountingpointcharacteristics` |
  | RSM-012 | `/notifyvalidatedmeasuredata` |
  | RSM-015 | `/requestvalidatedmeasurements` |
  | RSM-016 | `/requestaggregatedmeasuredata` |
  | RSM-017 | `/requestwholesalesettlement` |
  | RSM-020 | `/requestservice`, `/confirmrequestservice`, `/rejectrequestservice` |
  | RSM-021 | `/requestchangeaccountingpointcharacteristics` |
  | RSM-024 | `/requestcancellation` |
  | RSM-027 | `/requestchangecustomercharacteristics` |
  | RSM-030 | `/requestchangebillingmasterdata` |
  | RSM-032 | `/requestbillingmasterdata` |
  | RSM-033 | `/requestchangeofpricelist` |
  | RSM-035 | `/requestpricelist` |

  **WattsOn had RSM-027, RSM-032 and RSM-035 wrong**: all three were sent to `/requestservice`. RSM-003 and RSM-024 were missing.
- HTTP outcomes:
  - `202` → accepted (synchronous validation passed).
  - `400` → failed synchronous validation, do not retry.
  - `415` → wrong content type.
  - `401` → re-authenticate.
  - `403` with an HTML body → blocked by the firewall. Log the `x-azure-ref` header and give it to support.
  - `503` → maintenance, retry.
- **Asynchronous validation** errors arrive later as **RSM-009** on `/measuredata`. A 202 does not mean the content was accepted.
- Log the `correlationId` response header on every call.

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

### 3.6 Where the authoritative examples are
- **JSON and XML examples** are attached to each BRS-guide page on Confluence, plus a ZIP per BRS. For example, BRS-001 has RSM-001 Request (Privat/Erhverv), Confirm and Reject, RSM-022, RSM-024, RSM-027, RSM-028, RSM-031 and RSM-004. ZIPs also exist for BRS-002, 004, 009, 010, 031, 034, 037, 038 and 041. **Download all of them as the platform's first test fixtures.**
- A **Postman collection** is attached to the CIM webservice page. It is served from `api.media.atlassian.com`, so download it in a browser.
- No formal JSON schema files were found on Confluence.
- Energinet's DataHub 3 source on GitHub (`Energinet-DataHub`) may contain the message definitions [not checked].

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
- RSM-027/032/035 were posted to the wrong endpoint (`/requestservice`, see §3.2).
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

## 6. Approval scope (from Confluence, read 2026-11-11)

### 6.1 Approval procedure
- Phases 1 and 2 are closed. Phase 2 approval was based on **RSM-012 metered data**. The **phase 3 actor test started 19 Jan 2026**; test cases have been published since 1 Dec 2025.
- An elleverandør with its own system runs the test cases for the messages it uses or expects to use. An IT vendor runs the cases its system supports, **for at least 2 actors per market role/protocol**.
- **How to submit:** email a case to `datahub@energinet.dk` asking for DataHub 3 approval. It must confirm that our own system handles the relevant messages correctly, not just that B2B is connected.
- Phase 3 test actors provided by Energinet: Ellev_1A `8100000000108`, Ellev_1B `8100000000115`, Ba_1A `8100000000207`, ENDK grid area 804 `8100000000047`. Users for them are assigned through DataHub Support.

### 6.2 Phase 2 test cases (closed, mandatory for all)
- **Mandatory:** receive RSM-012 (E23) at 15 and 60 min for metering point types E18, E17, D05, D06, D07, D08, D09, D10, D11, and at 60 min only for D14, D15, D19.
- **Conditional:** RSM-015 (E23 at 15/60 min, E30 annual sum), only if BRS-024/025 are used.

### 6.3 Phase 3 test cases — elleverandør with its own system
Legend:
- 🟢 = must be done
- **B** = only if the system supports the process
- **SF** = voluntary
- **V** = self-check, not part of verification

Cases marked `-IT` are for IT vendors only.

| Group | Test cases (Ellev column) | BRS | Requirement |
|---|---|---|---|
| Change of supplier | E01 future supplier; E02 linked child MPs (V); E03 cancellation | 001 | 🟢 |
| End of supply | E06 current supplier | 002 | 🟢 |
| Erroneous change of supplier | E10 current supplier starts, previous supplier accepts; E12 as previous supplier, customer comes back | 003 | 🟢 |
| Master data request | E14B future supplier | 005 | B |
| MP master data update | E16 to current supplier; E17B to future supplier | 006 | SF (voluntary) |
| MP closed down | E18 current supplier; E19 future supplier | 007 | 🟢 |
| New MP connected | E20 | 008 | 🟢 |
| Move-in | E21 normal; E22 secondary; E23 move-in by another supplier (notified as current supplier) | 009 | 🟢 |
| Move-out | E26 | 010 | 🟢 |
| Erroneous move | E28 accepted; E30 as previous supplier | 011 | 🟢 |
| Disconnect / reconnect | E31 | 013 | 🟢 |
| Customer master data | E32 name and contact details | 015 | 🟢 |
| Price notifications | E33–E39: tariff hourly, subscription monthly, price series, irregular series, stop, cancellation of stop (prices update on Tuesdays) | 031 | 🟢 |
| Price request | E40B price info; E41B price series | 034 | B |
| Price link notifications | E42–E47: subscription, fee, tariff, quantity change, stop, Energinet tax tariff (grid area 804) | 037 | 🟢 |
| Price link request | E48B back in time; E49B today | 038 | B |
| Service request | E50B disconnect; E51B reopen | 039 | B |
| Electric heating | E52B set; E53 remove; E54/E55 HTX set/remove | 041 | 🟢 |

Withdrawn or not applicable to an elleverandør's own system: E04-IT, E05/E05A (on pause), E07-IT, E08-IT, E09 (withdrawn), E11-IT, E13-IT, E15-IT, E24-IT, E25-IT, E27-IT, E29-IT, E56 (BRS-044, withdrawn), and Aktørdialog (withdrawn).

The table does not give the RSM or business reason per test case. Take them from the BRS-guide pages and their example ZIPs.

### 6.4 Our scope: IT vendor, elleverandør, no optional processes

The IT-Lev column applies, so this is the list to pass, for each of 2 actors:

| BRS | Test cases | Note |
|---|---|---|
| 001 | E01, E02 (V), E03, **E04-IT** (defaulted process) | E05/E05A are on pause — watch for them to be released |
| 002 | E06, **E07-IT** (rejected by grid company), **E08-IT** (cancellation) | |
| 003 | E10, **E11-IT** (previous supplier rejects), E12 | |
| 004 | **E13-IT** (new child MP) | "S": open a support case if the system can't do it |
| 006 | E16, E17B | Required for IT vendors ("s" = support case if not possible); voluntary only for own-system actors |
| 007 | E18, E19 | |
| 008 | E20 | |
| 009 | E21, E22, E23, **E24-IT** (cancellation), **E25-IT** (new MP) | |
| 010 | E26, **E27-IT** (cancellation) | |
| 011 | E28, **E29-IT** (rejected), E30 | |
| 013 | E31 | |
| 015 | E32 | |
| 031 | E33–E39 | Prices update on Tuesdays — plan test runs around it |
| 037 | E42–E47 | E47 uses grid area 804 |
| 041 | E52B, E53, E54, E55 | E52B is 🟢 despite the "B" in its ID |
| — | Phase 2 baseline: RSM-012 (E23), all MP types, 15 + 60 min | |

Out of scope, because they are optional (B): E14B, E15-IT (BRS-005), E40B/E41B (034), E48B/E49B (038), E50B/E51B (039). E09, E56 and Aktørdialog are withdrawn.

About 45 test cases × 2 actors ≈ 90 recorded preprod runs. **The evidence log must be built in, not assembled by hand afterwards.**

Process capabilities this implies:
- **Send:** RSM-001 (E03 change of supplier, E65 move-in, BRS-003), RSM-005 (end of supply / move-out / erroneous move), RSM-024 cancellation, RSM-027 customer master data, RSM-003 confirm/reject (reallocation, as previous supplier).
- **Receive:** confirm/reject replies, RSM-004 notifications, metering point master data (RSM-022), RSM-012 metered data, RSM-009 validation errors, BRS-031/037 prices and price links, BRS-041 electric heating, BRS-013 disconnect/reconnect.

### 6.5 Our preprod test data (from Energinet, 30 Mar 2026)

**Phase 2 — `info-til-aktører-0114-8200000008361-fase-2.xlsx`**
- 15 metering points in grid area 804, all connected (E22), supplied by **GLN `8200000008361`** since 2023-01-01. This is our actor.
- E17 consumption (8): 3 at PT15M and 1 at PT1H with settlement method E02; 4 at PT1H with D01.
- E18 production (7): all PT15M.
- Test customers: CPR `1111111415` / `1111111416`.
- Energinet sent RSM-012 for some of them, so they should be waiting on `/measuredata`.
- **Gap:** the phase 2 test cases also list MP types D05–D11, D14, D15 and D19. This file has only E17 and E18. Ask Energinet whether more are needed.

**Phase 3 — `Info_138-fase-3.xlsx`**
- 50 metering points in grid area 804, valid from 2026-01-01, with **no supplier**. Almost all are in connectionState **D03** (not yet connected).
  - These are the targets for change of supplier, move-in, and "new MP connected" (BRS-008 / E20).
- Parents: E17 ×26 (15 at PT15M, 11 at PT1H) and E18 ×14 (PT15M, `psrType D12`).
- Children: D06 ×2, D07 ×2, D17 ×2, D18 ×2, and D19 ×2 (these two connected, E22). The children cover the "linked child MPs" (E02) and child-MP cases.
- The columns use CIM names and the value wrapping (`A10,<GSRN>`, `KWT,230.2`, `AMP,64`). This matches the RSM-022 master data shape, so use them to check our master data parser.

**Still missing for the IT-vendor approval:** a second actor with its own test metering points.

### 6.6 Suggested build order

| Order | Item | Done when |
|---|---|---|
| 1 | Transport: per-actor auth, peek/dequeue on 3 queues (max 1 peek/min per queue), outbox, raw store, RSM-009 handling, dead-letter | Preprod documents fetched, stored, dequeued; a send gets 202 |
| 2 | Message layer from Energinet's example ZIPs (all RSMs in §6.3) | Round-trip tests pass on every example |
| 3 | Metered data RSM-012, all MP types, PT15M + PT1H (phase 2 baseline) | The phase 2 rows pass |
| 4 | Requests we start: BRS-001/002/003/009/010/011 including cancellations and the -IT variants (E01–E04, E06–E08, E10–E12, E21–E30) | Preprod runs recorded for both actors |
| 5 | Notifications in: BRS-004/006/007/008 (E13, E16–E20), BRS-013 (E31), BRS-031 (E33–E39), BRS-037 (E42–E47), BRS-041 (E52–E55) | Preprod runs recorded for both actors |
| 6 | BRS-015 customer master data (E32) | Preprod runs recorded for both actors |
| 7 | Ops view and evidence export per test case and actor | Approval case sent to `datahub@energinet.dk` |

---

## 7. Sources

Energinet Confluence (DHDOCS), read 2026-11-11:
- [Test cases for Elleverandør, fase 3](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/2663546892)
- [Aktørtest fase 3](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1751941156) · [Testcases fase 3](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1752367107) · [Leveranceplan fase 3](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1751580719)
- [Dokumenttyper og Endpoints](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/654147782) · [CIM webservice interface](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/653983967) · [Dokumenttype og deres endpoint](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1450475626)
- [Validering af beskeder](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1508605997) · [Aktørtestmiljø fase 2](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1364197377) · [Godkendelse fase 1](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1013284942)
- [BRS-001 Leverandørskift](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/152961338) (example attachments)

Other:
- [Forskrift F1: EDI-kommunikation med DataHub i elmarkedet](https://www.energinet.dk/media/2a2jksg2/18-03427-12-forskrift-f1_energinet-hoering.pdf) — system test plus actor test required
- [Vilkår for adgang til og brug af DataHub – Elleverandør](https://www.energinet.dk/media/bzzkzwh5/vilkaar-for-adgang-til-og-brug-af-datahub-elleverandoer-2021.pdf)
- [Godkendelse som It-leverandør eller aktør til DataHub 3 – Fase 2](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1228570770/Godkendelse+som+It-leverand+r+eller+akt+r+til+DataHub+3+-+Fase+2) — own actor vs. IT vendor (≥ 2 actors per role/protocol), submit to `datahub@energinet.dk`
- [Test Cases – Elleverandør](https://energinet.atlassian.net/wiki/spaces/DHDOCS/pages/1228570791/Test+Cases+-+Elleverand+r) — run the cases for the messages used in production
- [DAB referat december 2023](https://www.energinet.dk/media/2bej5bag/referat-dab-december-2023.pdf) — actor test made mandatory
