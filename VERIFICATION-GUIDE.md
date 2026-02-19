# WattsOn Verification Guide 🔌

Your personal tour of WattsOn's features. Open `http://localhost:5173` and follow along.

---

## Before You Start

The database is fresh — no data yet. That's by design. WattsOn proves itself by processing messages, not seed data. You'll build up the system state as you go through each scenario.

**Frontend:** http://localhost:5173  
**API:** http://localhost:5100

---

## Tour 1: Set Up Your Supplier Identity

**Where:** Admin page (sidebar → Admin)

This is the first thing any electricity supplier does — register their GLN(s) with the system.

1. Go to **Admin**
2. Create a supplier identity:
   - **GLN:** `5790001330552` (our test GLN)
   - **Name:** `WattsOn Energi A/S`
   - **CVR:** `12345678`
   - **Active:** Yes
3. Verify it appears in the list

**What this proves:** Supplier identity management, GLN validation with check digits, the foundation for all BRS processes.

---

## Tour 2: Simulation — Supplier Switch (Incoming)

**Where:** Simulation page (sidebar → Simulation)

This is the flagship demo. A customer switches from another supplier to us. The system runs through the entire BRS-001 process flow.

1. Go to **Simulation**
2. Scenario should default to **Skift · Ind** (incoming supplier switch)
3. Click **Tilfældig** (Random) to generate a Danish customer with realistic data
4. Make sure **Generer forbrugsdata** is toggled ON
5. Click **Kør leverandørskift · indgående**
6. Watch the process steps execute in real-time:
   - RSM-001/E03 request → DataHub validation → Confirmation → Master data → Supply created
   - Time series loaded (realistic Danish household consumption pattern)
   - **Settlement Engine auto-calculates** (polls every 30s — may take up to 30s)

**What to look for:**
- The step-by-step process visualization
- Settlement breakdown with charge lines, quantities, unit prices, and total DKK amount
- Links to navigate directly to Customer, Settlement, and Processes views

**What this proves:** BRS-001 end-to-end, customer/metering point creation, time series generation, auto-settlement calculation, price link association.

---

## Tour 3: Explore the Customer

**Where:** Click "Se customer" from the simulation result, or go to Customers page

1. From the simulation result, click **Se customer**
2. You'll see the Customer Detail page with:
   - Customer info (name, CPR, email, address)
   - Active supplies with metering point details
   - Associated processes (BRS-001 should be listed)
   - Settlement history
3. Go back to **Customers** list — your customer should appear

**What this proves:** Customer hub design, supply-to-customer relationship, process audit trail.

---

## Tour 4: Metering Points & Time Series

**Where:** Metering Points page (sidebar → Målepunkter)

1. Go to **Målepunkter**
2. Your simulated metering point should appear with GSRN, grid area, active supply status
3. Click into the detail view
4. You'll see:
   - Metering point master data (type, resolution, settlement method, connection state)
   - Action buttons for BRS processes (BRS-005, 039, 041, 025, 024, 038)
   - Linked time series
   - Linked prices

**What this proves:** Metering point management, GSRN handling, action buttons for send-side processes.

---

## Tour 5: Settlements & The Settlement Engine

**Where:** Settlements page (sidebar → Afregning)

1. Go to **Afregning** (Settlements)
2. You should see the auto-calculated settlement from the simulation
3. Click into the settlement detail to see:
   - Period, metering point, total energy
   - Line-by-line breakdown (each price × quantity = amount)
   - Settlement status (Pending → ready for external invoicing system to pick up)

**What this proves:** Auto-settlement from time series + prices, settlement document model, Peppol-aligned document numbering.

---

## Tour 6: Add Prices (Supplier Margin)

**Where:** Prices page (sidebar → Priser)

Regulated tariffs come from DataHub via BRS-031/037. But as a supplier, you also set your own margin.

1. Go to **Priser**
2. Currently shows tariffs/prices linked to your metering point (if any were auto-linked)
3. Use the API to create a supplier margin price:

```bash
curl -X POST http://localhost:5100/api/prices \
  -H "Content-Type: application/json" \
  -d '{
    "chargeId": "MARGIN-01",
    "ownerGln": "5790001330552",
    "type": "Tarif",
    "description": "Leverandørmargin",
    "validFrom": "2026-01-01T00:00:00Z",
    "pricePoints": [
      {"timestamp": "2026-01-01T00:00:00Z", "price": 0.15}
    ]
  }'
```

4. Refresh the Prices page — your margin should appear
5. Filter by type (Tarif / Gebyr / Abonnement) using the stats cards

**What this proves:** Price management, Danish price type enums, price point time series, read-only UI with filters.

---

## Tour 7: Run More Simulations

### 7a: Tilflytning (Move-In)

1. Go to **Simulation** → select **Tilflytning**
2. Click **Tilfældig** and **Kør**
3. Creates a new customer moving into a metering point
4. Same flow as supplier switch but with BRS-009 process type (E65 business reason)

### 7b: Leverandørskift Udgående (Outgoing Switch)

1. Select **Skift · Ud**
2. Pick one of your active supplies from the dropdown
3. **Kør** — simulates losing a customer to another supplier
4. Supply ends, process tracked

### 7c: Fraflytning (Move-Out)

1. Select **Fraflytning**
2. Pick an active supply
3. **Kør** — customer moves out, supply terminated

**What this proves:** All 4 customer lifecycle scenarios, bidirectional BRS-001/009 handling.

---

## Tour 8: Processes & Messages

**Where:** Processes page (sidebar → Processer) and Messages page (sidebar → Beskeder)

1. Go to **Processer** — all BRS processes from your simulations should be listed
2. Each shows: type, GSRN, status, state transitions, timestamps
3. Check the **New Request** dropdown for send-side processes (BRS-023, 027, 034)
4. Go to **Beskeder** (Messages) — inbox messages from simulations appear here
5. Go to **Outbox** — any outbound CIM messages (from send-side processes)

**What this proves:** Full process audit trail, inbox/outbox pattern, state machine tracking.

---

## Tour 9: Send-Side Processes (from Metering Point Detail)

**Where:** Metering Point Detail → Action buttons

1. Go to a metering point detail page
2. Try the action buttons:
   - **BRS-005**: Request master data from DataHub
   - **BRS-039**: Service request (disconnect/reconnect/meter investigation)
   - **BRS-025**: Request historical metered data
   - **BRS-024**: Request yearly consumption sum
   - **BRS-038**: Request charge links
3. Each creates an outbox message with a production CIM JSON envelope
4. Check the **Outbox** page to see the queued messages

**What this proves:** Outbound CIM envelope generation, RSM document types, DGL/DDZ receiver roles, NDK grid area coding.

---

## Tour 10: Supplies Page

**Where:** Supplies page (sidebar → Leverancer)

1. Go to **Leverancer**
2. See all supplies across customers — active and ended
3. Action buttons for:
   - **BRS-002**: End of supply (request disconnection)
   - **BRS-010**: Move-out (immediate end)
   - **BRS-003**: Incorrect switch (dispute within 60 days)
   - **BRS-011**: Incorrect move (dispute)

**What this proves:** Supply lifecycle management, send-side process initiation from supply context.

---

## Tour 11: Dashboard Overview

**Where:** Dashboard (sidebar → Dashboard)

1. Go to **Dashboard**
2. See aggregate stats: customers, metering points, active supplies, settlements
3. The numbers should match what you've created through simulations

**What this proves:** System overview, aggregate queries across all entities.

---

## Tour 12: Outbox & DataHub Dispatch

**Where:** Outbox page (sidebar → Outbox)

1. Go to **Outbox**
2. See all queued outbound messages
3. Each shows: RSM type, recipient, status, retry count
4. **Retry** button available for failed messages
5. Currently in **simulation mode** (no DataHub credentials) — messages log what would be sent

**What this proves:** Outbox dispatch pattern, exponential backoff, dead-letter handling, retry mechanism.

---

## Bonus: API Exploration

The system exposes a comprehensive REST API. Some highlights:

```bash
# Dashboard stats
curl http://localhost:5100/api/dashboard | jq

# All settlements (uninvoiced)
curl http://localhost:5100/api/settlements/uninvoiced | jq

# Settlement documents (Peppol-aligned)
curl http://localhost:5100/api/settlement-documents | jq

# Spot prices (from Energi Data Service)
curl http://localhost:5100/api/spot-prices/latest | jq

# Run reconciliation (compare our settlements vs DataHub wholesale data)
curl -X POST http://localhost:5100/api/reconciliation/run \
  -H "Content-Type: application/json" \
  -d '{"gridArea": "DK1", "startDate": "2026-02-01T00:00:00Z", "endDate": "2026-03-01T00:00:00Z"}'

# Health check
curl http://localhost:5100/api/health
```

---

## What You're Looking At

| Feature | BRS Processes | What It Does |
|---------|--------------|--------------|
| Customer lifecycle | BRS-001, 009 | Supplier switch, move-in/out |
| Supply management | BRS-002, 003, 010, 011 | End supply, incorrect switch/move |
| Master data | BRS-004, 005, 006, 007, 008, 013 | MP create/update/decommission/connect |
| Metered data | BRS-021, 025 | Time series, historical data requests |
| Prices & tariffs | BRS-031, 034, 037, 038 | Price updates, charge links |
| Data requests | BRS-023, 024, 027 | Aggregated/wholesale data from DataHub |
| Service requests | BRS-039, 041 | Disconnect/reconnect, electrical heating |
| Special | BRS-015, 036, 044 | Customer update, product obligation, forced switch |
| **Settlement engine** | — | Auto-calculates from time series + prices |
| **Reconciliation** | — | Compare our settlements vs DataHub data |
| **CIM outbox** | 9 RSM types | Production CIM JSON for DataHub B2B |

**28 BRS processes. 479 tests. Zero seed data. One `docker compose up`.**
