# Verification: Correction Detection ⭐ (Core Value Proposition)

## Scenario

DataHub sends updated time series for an already-invoiced period. This happens in production when:
- A smart meter is recalibrated and historical data is recalculated
- Estimated readings are replaced with actual measurements
- A grid company discovers a measurement error

WattsOn must automatically:
1. Detect it's a correction (same metering point + overlapping period, but new time series version)
2. Calculate the delta (new values − old values)
3. Create an adjustment settlement linked to the original
4. Make the adjustment available for the external invoicing system as a credit/debit note

This is WattsOn's **core value proposition** — the thing that justifies the system's existence. Without automatic correction detection, a supplier would need to manually monitor DataHub for changes and recalculate settlements by hand.

This covers: **BRS-021 (corrected metered data) → Correction Detection → SettlementCalculator (delta) → Adjustment Settlement**

---

## What's Real vs Simulated

| Component | Status | Explanation |
|---|---|---|
| Correction detection logic | ✅ Real | SettlementWorker's exact production code — detects new time series version for invoiced period |
| Delta calculation | ✅ Real | SettlementCalculator.CalculateCorrection — pure domain math, no shortcuts |
| Time series versioning | ✅ Real | Old version marked as superseded (IsLatest=false), new version is latest |
| Settlement status transitions | ✅ Real | Original: Invoiced → Adjusted. New correction: Calculated (ready for invoicing) |
| Credit/debit note classification | ✅ Real | Negative delta → creditNote, positive delta → debitNote |
| Adjustment settlement entity | ✅ Real | Full Settlement entity with IsCorrection=true, linked to PreviousSettlementId |
| CIM inbox message (BRS-021/RSM-012) | 🔸 Simulated | Real envelope format (NotifyValidatedMeasureData_MarketDocument, type E66, process E23) but generated locally |
| DataHub transmission | 🔸 Simulated | In production, DataHub would send RSM-012 via Peek/Dequeue. Here we generate and store it directly |
| Consumption variation (±5-15%) | 🔸 Simulated | Real corrections vary by specific amounts. We apply random ±5-15% per hour which is realistic for recalibrations |
| Quality code (A05 = Revised) | ✅ Real | Correct ebIX quality code for revised measurements |

**The key point:** The correction detection and delta calculation are 100% production code. The SettlementWorker runs the same logic it would in production — finding the new unsettled time series, checking for an existing invoiced settlement, and calling SettlementCalculator.CalculateCorrection.

---

## Prerequisites

You must complete **Scenario 001** and **Scenario 002** first:

1. ✅ Scenario 001 completed: Customer created, time series generated, settlement calculated
2. ✅ Scenario 002 completed: Settlement marked as "Invoiced" with an invoice reference

**Why:** The correction detection only kicks in when there's an existing **invoiced** settlement for the same metering point and period. If the settlement is still "Calculated", the SettlementWorker would just recalculate it (overwrite), not create a correction.

---

## Step-by-Step Walkthrough

### Step 1: Confirm You Have an Invoiced Settlement

Navigate to **http://localhost:5173** → sidebar → **Settlements**.

You should see a settlement with:
- **STATUS:** Blue "invoiced"
- **KUNDE:** Your customer from Scenario 001

If you don't see an invoiced settlement, go back to Scenario 002 and complete it first.

### Step 2: Open the Simulator

Navigate to sidebar → **Simulator** (under SYSTEM group, flask icon).

Scroll down past the main simulation section. You'll see a new card:

**"Simuler korrigeret data"** with a red border and "Scenarie 003" tag.

The card shows:
- Description: "Simuler at DataHub sender korrigerede målinger for en allerede faktureret periode..."
- A dropdown: **"Vælg faktureret settlement"**

### Step 3: Select a Settlement to Correct

The dropdown shows your invoiced settlements. Each option shows:
- **Customer name** — the customer from Scenario 001
- **GSRN** — the metering point number
- **Amount** — the invoiced amount in DKK

Select the settlement. A blue info card appears below with details:

| Field | Value |
|---|---|
| Kunde | Customer name |
| GSRN | 18-digit metering point number |
| Periode | e.g., 1. feb. 2026 — 1. mar. 2026 |
| Forbrug | e.g., 648.1 kWh |
| Beløb | e.g., 988,31 DKK |
| Faktura | Blue tag with your invoice reference (e.g., INV-2026-0042) |

### Step 4: Simulate the Correction

Click the red **"Simuler korrektion"** button.

The right side of the card shows a vertical stepper with the correction process executing in real-time:

---

**Step 4a: "DataHub sender korrigeret data"**
- Status: spinner → ✓ done
- Detail: *"RSM-012 received — ny version af måledata for perioden"*
- 🔸 **Simulated:** In production, DataHub would send this via Peek/Dequeue. The CIM message type is `NotifyValidatedMeasureData_MarketDocument` with:
  - Type code: E66 (validated measure data)
  - Process type: E23 (metered data collection)
  - Quality: A05 (Revised)

**Step 4b: "Genererer korrigerede målinger"**
- Status: spinner → ✓ done
- Detail: *"Version 1 → 2. 648.1 → 673.5 kWh (+3.9%)"* (numbers vary)
- 🔸 **Simulated:** Each hourly value gets ±5-15% random variation. The total delta depends on random distribution — some hours go up, some go down.

**Step 4c: "CIM besked oprettet"**
- Status: spinner → ✓ done
- Detail: *"NotifyValidatedMeasureData — 672 timeværdier, kvalitet A05 (Revised)"*
- 🔸 **Simulated:** A full CIM JSON envelope is created and stored in the inbox. In production, this message would arrive from DataHub's market message queue.

**What actually happened behind the scenes:**
1. The original time series (version 1) was marked as **superseded** (IsLatest = false)
2. A new time series (version 2) was created with varied hourly values
3. A realistic CIM inbox message was created with proper BRS-021/RSM-012 envelope
4. The new time series is now the **latest** version and has no matching settlement

**Step 4d: "Settlement Engine registrerer"**
- Status: spinner with *"Venter på SettlementWorker... (5s)"* → updates every 5s → ✓ done
- Detail: *"Korrektion registreret — original settlement markeret som 'Adjusted'"*
- ✅ **Real — this is 100% production code.** The SettlementWorker:
  1. Polls every 30 seconds for unsettled time series
  2. Finds the new version 2 (IsLatest=true, no matching settlement)
  3. Looks for an existing invoiced settlement for the same metering point + period
  4. **Detects the correction** — finds the invoiced settlement from Scenario 002
  5. Marks the original settlement as **Adjusted** (status: Invoiced → Adjusted)
  6. Calls `SettlementCalculator.CalculateCorrection()` to compute the delta
  7. Creates a new correction settlement linked to the original via PreviousSettlementId

**⏱ Note:** This step takes up to 30 seconds because the SettlementWorker polls on a 30-second interval.

**Step 4e: "Justering beregnet"**
- Status: ✓ done
- Detail: *"WO-2026-00002: Delta 25,40 DKK (Debitnota)"* (numbers vary)
- ✅ **Real:** The correction settlement has been calculated and saved. The delta represents the difference between the new and old consumption × prices.

---

### Step 5: Inspect the Correction Result

After all steps complete, a red result card appears:

**"Korrektionsresultat"** with a tag showing either:
- **Debitnota** (orange) — if total delta is positive (customer consumed more → owes more)
- **Kreditnota** (green) — if total delta is negative (customer consumed less → gets refund)

**Details:**

| Field | Example Value |
|---|---|
| Original | 648.1 kWh |
| Korrigeret | 673.5 kWh |
| Delta energi | +25.4 kWh (+3.9%) |
| Version | v1 → v2 |

*(Your numbers will differ — they depend on random variation.)*

**Buttons:**
- **"Se justering"** (red) — opens the correction settlement detail page
- **"Alle settlements"** — goes to the settlements list

### Step 6: View the Correction Settlement

Click **"Se justering"**.

You land on the Settlement Detail page for the **correction** settlement:

**Header:**
- Document ID: `WO-2026-00002` (next sequential number)
- Tags: "Debitnota" (orange) or "Kreditnota" (green) + "Calculated" (green)
- If debit: "Korrigerer: WO-2026-00001" text showing the link to the original

**Correction link section:**
- "Original settlement: **WO-2026-00001**" — clickable link back to the original

**Line items table (delta amounts):**

Each line shows the **adjustment** (delta) amount, not the full amount:

| # | BESKRIVELSE | MÆNGDE | ENHEDSPRIS | BELØB |
|---|---|---|---|---|
| 1 | Nettarif C-kunde (justering) | 25.4 kWh | 0,2616 DKK | 6,65 DKK |
| 2 | Systemtarif (justering) | 25.4 kWh | 0,0540 DKK | 1,37 DKK |
| 3 | Transmissionstarif (justering) | 25.4 kWh | 0,0490 DKK | 1,24 DKK |
| 4 | Elafgift (justering) | 25.4 kWh | 0,0080 DKK | 0,20 DKK |
| 5 | Leverandørmargin (justering) | 25.4 kWh | 0,1500 DKK | 3,81 DKK |

**Note:** The subscription (Abonnement) line does NOT appear — because the correction only affects energy-based charges. The subscription fee is days-based and doesn't change when consumption changes.

**Summary:**
- Total inkl. moms = delta amount × 1.25 (VAT)

The "Bekræft fakturering" button is available — this correction is ready to be invoiced by the external system as a debit/credit note.

### Step 7: View the Original Settlement (Now Adjusted)

Click the "Original settlement: WO-2026-00001" link.

The original settlement now shows:
- **Status:** "Adjusted" (orange tag) — no longer "Invoiced"
- **Invoice reference still visible:** "Invoiced som INV-2026-0042" with timestamp
- **New link:** "Korrektion oprettet: **WO-2026-00002**" — clickable link to the correction

**This is the critical state transition:**
- The original settlement went: Calculated → Invoiced → **Adjusted**
- The correction settlement starts: **Calculated** (ready for the external invoicing system)

### Step 8: Verify on the Settlements List

Navigate to sidebar → **Settlements**.

You should now see:

**Runs tab:**
- Original settlement: Blue "invoiced" → now Orange "adjusted"
- The correction does NOT appear here (it's not a "run" — it's a correction)

**Corrections tab:**
Click the "Corrections" tab. You should see:
- The correction settlement: Green "calculated" with an orange "debit" (or green "kredit") tag
- This correction is ready to be invoiced

**Summary stats:**
- "Corrections" counter should show ≥ 1 (in red if > 0)
- "Ready to Invoice" includes the correction (it's Calculated)

### Step 9: Verify via API (External System Perspective)

**Corrections endpoint — external system picks up credit/debit notes:**
```bash
curl http://localhost:5100/api/settlement-documents?status=corrections | jq
```

Returns the correction settlement with:
```json
{
  "documentType": "debitNote",  // or "creditNote"
  "documentId": "WO-2026-00002",
  "originalDocumentId": "WO-2026-00001",
  "previousSettlementId": "...",
  "status": "Calculated",
  "totalExclVat": 13.27,
  "totalVat": 3.32,
  "totalInclVat": 16.59,
  "lines": [
    {
      "description": "Nettarif C-kunde (justering)",
      "quantity": 25.4,
      "lineAmount": 6.65,
      "taxCategory": "S",
      "taxPercent": 25.0,
      "taxAmount": 1.66
    }
  ]
}
```

**All settlements (shows the full picture):**
```bash
curl http://localhost:5100/api/settlement-documents?status=all | jq
```

You should see:
1. Original settlement — status "Adjusted", with invoice reference preserved
2. Correction settlement — status "Calculated", documentType "debitNote" or "creditNote"

### Step 10: Invoice the Correction (Optional)

You can now mark the correction as invoiced too:

1. Navigate to the correction settlement detail page
2. Click "Bekræft fakturering"
3. Enter reference: `CN-2026-0001` (for credit note) or `DN-2026-0001` (for debit note)
4. Click "Bekræft"

The correction is now invoiced. The full lifecycle is complete.

---

## The Correction Detection Algorithm

Here's exactly what the SettlementWorker does (from `SettlementWorker.cs`):

```
1. Find time series where IsLatest=true AND no matching Settlement exists
2. For each unsettled time series:
   a. Find the active supply for this metering point
   b. Find active price links
   c. Check: does an INVOICED settlement exist for same metering point + same period?
      → YES: This is a correction!
        - Mark original settlement as Adjusted
        - Call SettlementCalculator.CalculateCorrection()
        - This calculates the DELTA (new minus old) per charge line
        - Create correction settlement with IsCorrection=true + PreviousSettlementId
      → NO: Normal settlement
        - Call SettlementCalculator.Calculate()
        - Create new settlement
```

**Critical design decision:** Corrections are only detected against **Invoiced** settlements. If a settlement is still "Calculated" (not yet invoiced), the SettlementWorker would create a new normal settlement alongside it. This prevents false positives during the initial settlement period.

---

## Settlement Breakdown: Example Correction Numbers

| Charge | Original (v1) | Corrected (v2) | Delta |
|---|---|---|---|
| Nettarif C-kunde | 648.1 kWh × 0.2616 = 169.55 | 673.5 kWh × 0.2616 = 176.18 | +25.4 kWh → +6.65 DKK |
| Systemtarif | 648.1 × 0.054 = 35.00 | 673.5 × 0.054 = 36.37 | +25.4 → +1.37 DKK |
| Transmissionstarif | 648.1 × 0.049 = 31.76 | 673.5 × 0.049 = 33.00 | +25.4 → +1.24 DKK |
| Elafgift | 648.1 × 0.008 = 5.18 | 673.5 × 0.008 = 5.39 | +25.4 → +0.20 DKK |
| Leverandørmargin | 648.1 × 0.15 = 97.22 | 673.5 × 0.15 = 101.03 | +25.4 → +3.81 DKK |
| Abonnement | 28 dage × 23.20 = 649.60 | 28 dage × 23.20 = 649.60 | 0 (no change) |
| **Subtotal** | **988,31 DKK** | **1.001,57 DKK** | **+13,27 DKK** |
| Moms (25%) | 247,08 DKK | 250,39 DKK | +3,32 DKK |
| **Total inkl. moms** | **1.235,39 DKK** | **1.251,96 DKK** | **+16,59 DKK** |

*(These are example numbers — your actual values will differ based on random variation.)*

---

## DataHub Mapping

| WattsOn Action | DataHub Equivalent | Notes |
|---|---|---|
| Corrected time series received | BRS-021/RSM-012 with new version | DataHub sends updated metered data when meters are recalibrated |
| Time series versioning | DataHub version numbering | Each correction increments the version |
| CIM envelope type E66 | NotifyValidatedMeasureData | Standard document type for metered data |
| Process type E23 | Metered data collection | DataHub business process identifier |
| Quality code A05 | Revised | ebIX quantity quality for corrected measurements |
| Correction detection | Not automated in most systems | **This is WattsOn's unique value** — most suppliers check manually |
| Credit/debit note | Peppol BIS Credit/Debit Note | Standard e-invoicing document types |

---

## CIM Message Format

The simulated inbox message uses the exact CIM JSON format that DataHub would send:

```json
{
  "NotifyValidatedMeasureData_MarketDocument": {
    "mRID": "unique-document-id",
    "type": { "value": "E66" },
    "process.processType": { "value": "E23" },
    "businessSector.type": { "value": "23" },
    "sender_MarketParticipant.mRID": { "codingScheme": "A10", "value": "5790000432752" },
    "sender_MarketParticipant.marketRole.type": { "value": "DDZ" },
    "receiver_MarketParticipant.mRID": { "codingScheme": "A10", "value": "5790001330552" },
    "receiver_MarketParticipant.marketRole.type": { "value": "DDQ" },
    "MktActivityRecord": [{
      "mRID": "transaction-id",
      "marketEvaluationPoint.mRID": { "codingScheme": "A10", "value": "571313XXXXXXXXXXX" },
      "marketEvaluationPoint.type": { "value": "E17" },
      "product": "8716867000030",
      "Period": {
        "resolution": { "value": "PT1H" },
        "timeInterval": { "start": "2026-02-01T00:00:00Z", "end": "2026-03-01T00:00:00Z" },
        "Point": [
          { "position": 1, "quantity": 0.542, "quality": { "value": "A05" } },
          { "position": 2, "quantity": 0.318, "quality": { "value": "A05" } }
        ]
      }
    }]
  }
}
```

Key fields:
- `E66` = Validated measure data
- `E23` = Metered data collection process
- `DDZ` = DataHub (metered data responsible)
- `DDQ` = Balance responsible / supplier
- `E17` = Consumption metering point
- `A05` = Revised (quality code for corrections)
- `8716867000030` = Active energy product code

---

## Summary

You just verified the complete correction detection flow:

1. ✅ Simulated corrected metered data from DataHub (BRS-021/RSM-012)
2. ✅ New time series version created with ±5-15% variation
3. ✅ Original time series marked as superseded (IsLatest=false)
4. ✅ CIM inbox message with production-grade envelope format
5. ✅ SettlementWorker automatically detected the correction (production code)
6. ✅ Original settlement marked as "Adjusted" (preserving invoice reference)
7. ✅ Delta calculated: new values minus old values per charge line
8. ✅ Correction settlement created as credit/debit note
9. ✅ Correction available for external invoicing system via API
10. ✅ Visual indicators on settlements list (corrections tab, debit/credit tags)
11. ✅ Bidirectional links between original and correction on detail pages

**This is the core value proposition:** WattsOn automatically detects when DataHub data changes for an already-invoiced period and creates the correct adjustment. Without this, a supplier would need to manually compare old and new data, recalculate every charge line, and create credit/debit notes by hand.
