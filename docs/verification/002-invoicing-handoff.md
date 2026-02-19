# Verification: Settlement → External Invoicing Handoff

## Scenario

An external invoicing system integrates with WattsOn to invoice customers for electricity consumption. The workflow:
1. Fetch uninvoiced settlements via API
2. View the Peppol-aligned settlement document with full line breakdown
3. Confirm the settlement with an external invoice reference
4. Settlement marked as invoiced, no longer appears in uninvoiced list

This covers: **Settlement Document API → Invoice Confirmation → Status Lifecycle**

---

## What's Real vs Simulated

| Component | Status | Explanation |
|---|---|---|
| Settlement calculation | ✅ Real | SettlementWorker calculated the settlement from real time series + prices |
| Settlement document (Peppol BIS) | ✅ Real | Same document model an external invoicing system would consume via API |
| VAT calculation (25% standard) | ✅ Real | Danish standard VAT rate applied to each line, with proper S/Z categorization |
| Invoice confirmation flow | ✅ Real | Same `POST /api/settlement-documents/{id}/confirm` endpoint production would use |
| Status lifecycle (Calculated → Invoiced) | ✅ Real | Domain entity enforces valid state transitions |
| Buyer/seller party information | ✅ Real | Real customer and supplier data from the database |
| External invoicing system | 🔸 Simulated | In production, an external system (e.g., e-conomic, Dinero, Billy) calls the API. Here we use the WattsOn UI |
| Invoice reference format | 🔸 Simulated | We enter a reference manually. In production, the external system provides its own reference |

**The key point:** The entire API contract is production-ready. An external invoicing system can integrate with exactly these endpoints today.

---

## Prerequisites

Before running this scenario, you must complete **Scenario 001 (Customer Onboarding)** first. You need at least one settlement in "Calculated" status.

- ✅ Scenario 001 completed: Customer created, time series generated, settlement calculated
- ✅ Settlement visible on the Settlements page with status "Calculated"

If you haven't done this yet, go back to `docs/verification/001-customer-onboarding.md` and run through it first.

---

## Step-by-Step Walkthrough

### Step 1: Verify Settlement Exists

Navigate to **http://localhost:5173** → sidebar → **Settlements** (under BILLING group).

You should see at least one settlement row:

- **STATUS:** Green dot with "calculated"
- **MÅLEPUNKT:** An 18-digit GSRN number
- **PERIODE:** Date range (e.g., 1.2.2026 — 1.3.2026)
- **KUNDE:** Customer name from Scenario 001
- **BELØB:** Amount in DKK (e.g., 988,31 DKK)
- **BEREGNET:** Timestamp

**Summary stats at the top should show:**
- "Ready to Invoice" counter ≥ 1 (in green)
- "Total runs" ≥ 1

### Step 2: Open Settlement Detail

Click the settlement row.

You land on the Settlement Detail page. This is the **Peppol BIS-aligned pre-invoice document**.

**Document header (top card):**
- Document ID: `WO-2026-00001` format
- Tag: "Settlement" (grey-blue) + "Calculated" (green)
- Total inkl. moms in the top-right corner (e.g., 1.235,39 DKK)

**Below the header, you should see:**
- A green **"Bekræft fakturering"** button — this is the Mark as Invoiced action

**Three party/detail cards:**

| Card | Content |
|---|---|
| **Sælger** (Seller) | WattsOn Energi A/S, CVR number, GLN 5790001330552 |
| **Køber** (Buyer) | Customer name, CPR/CVR, address |
| **Detaljer** | Period, GSRN (metering point), grid area (DK1), calculated timestamp |

**Line items table:**

| # | BESKRIVELSE | CHARGE ID | MÆNGDE | ENHEDSPRIS | BELØB | MOMS |
|---|---|---|---|---|---|---|
| 1 | Nettarif C-kunde | NT-C1 | ~648 kWh | 0,2616 DKK | ~169,55 DKK | 25% + amount |
| 2 | Systemtarif | SYS-01 | ~648 kWh | 0,0540 DKK | ~35,00 DKK | 25% + amount |
| 3 | Transmissionstarif | TSO-01 | ~648 kWh | 0,0490 DKK | ~31,76 DKK | 25% + amount |
| 4 | Elafgift | EA-01 | ~648 kWh | 0,0080 DKK | ~5,18 DKK | 25% + amount |
| 5 | Leverandørmargin | LM-01 | ~648 kWh | 0,1500 DKK | ~97,22 DKK | 25% + amount |
| 6 | Månedligt abonnement | ABN-01 | 28,00 kWh* | 23,2000 DKK | ~649,60 DKK | 25% + amount |

*Subscription uses days-in-period as quantity, not kWh.*

**Summary at bottom of table:**
- Subtotal excl. moms: ~988,31 DKK
- Moms (Standard 25%): ~247,08 DKK
- **Total inkl. moms: ~1.235,39 DKK**

*(Your exact numbers will differ based on the random consumption generated.)*

### Step 3: Verify the API Directly (Optional)

If you want to verify the API contract that an external invoicing system would use:

**List uninvoiced settlements:**
```bash
curl http://localhost:5100/api/settlement-documents?status=ready | jq
```

This returns an array of settlement documents with status "Calculated". Each document includes:
- `documentType`: "settlement"
- `documentId`: WO-YYYY-NNNNN format
- `seller`: Supplier identity with GLN
- `buyer`: Customer with address
- `lines[]`: Each charge with quantity, unitPrice, lineAmount, taxCategory, taxPercent, taxAmount
- `taxSummary[]`: Aggregated tax by category
- `totalExclVat`, `totalVat`, `totalInclVat`

**Get single document:**
```bash
curl http://localhost:5100/api/settlement-documents/{id} | jq
```

### Step 4: Mark as Invoiced

Back on the Settlement Detail page, click the green **"Bekræft fakturering"** button.

A modal appears:

- Title: **"Bekræft fakturering"**
- Text: "Bekræft at WO-2026-00001 (1.235,39 kr.) er faktureret i det eksterne system."
- Input field: **"Ekstern fakturareference"** with placeholder "f.eks. INV-2026-0042"

Type an invoice reference: `INV-2026-0042`

Click **"Bekræft"**.

**What happens:**
1. ✅ **Real:** The system calls `POST /api/settlement-documents/{id}/confirm` with the invoice reference
2. ✅ **Real:** The domain entity transitions from `Calculated` → `Invoiced`
3. ✅ **Real:** The `InvoicedAt` timestamp is set to now
4. ✅ **Real:** The `ExternalInvoiceReference` is stored

**You should see:**
- Success message: "Fakturering bekræftet"
- The green "Bekræft fakturering" button disappears
- A new section appears below the header:
  - ✅ Check icon + "Invoiced som **INV-2026-0042**" with timestamp
- Status tag changes from "Calculated" (green) to "Invoiced" (blue)

### Step 5: Verify Status Change on List Page

Navigate back to **Settlements** (sidebar or browser back).

The settlement row now shows:
- **STATUS:** Blue dot with "invoiced"
- Everything else is the same

**Summary stats at the top:**
- "Ready to Invoice" counter should be 1 less (or 0)

### Step 6: Verify via API (External System Perspective)

**Uninvoiced settlements (should no longer include this one):**
```bash
curl http://localhost:5100/api/settlement-documents?status=ready | jq
```

The settlement you just confirmed should **not** appear in the "ready" list.

**All settlements (should show with Invoiced status):**
```bash
curl http://localhost:5100/api/settlement-documents?status=all | jq
```

The settlement should now have:
```json
{
  "status": "Invoiced",
  "externalInvoiceReference": "INV-2026-0042",
  "invoicedAt": "2026-02-19T..."
}
```

**Confirm endpoint (trying to double-confirm):**
```bash
curl -X POST http://localhost:5100/api/settlement-documents/{id}/confirm \
  -H "Content-Type: application/json" \
  -d '{"externalInvoiceReference":"INV-DUPLICATE"}' | jq
```

This should return **409 Conflict** with error message "Cannot mark as invoiced — status is Invoiced, expected Calculated".

### Step 7: Verify Cannot Re-Invoice

On the Settlement Detail page, the "Bekræft fakturering" button should no longer appear. The status is "Invoiced" and the invoice reference is displayed with the confirmation timestamp.

This is a guard rail — once invoiced, the settlement can only be modified through the correction flow (Scenario 003).

---

## What the External Invoicing System Would Do

In production, an external invoicing system (e.g., e-conomic, Dinero, or a custom billing system) would:

1. **Poll** `GET /api/settlement-documents?status=ready` periodically
2. For each new document, **create an invoice** in their system using the Peppol BIS data:
   - Buyer/seller party information → invoice parties
   - Lines with tax categories → invoice lines with VAT
   - Tax summary → invoice tax totals
3. **Confirm** back to WattsOn: `POST /api/settlement-documents/{id}/confirm` with their invoice number
4. **Monitor** `GET /api/settlement-documents?status=corrections` for adjustment settlements that need credit/debit notes

The API contract supports this entire workflow without any changes.

---

## DataHub Mapping

| WattsOn Concept | DataHub Equivalent | Notes |
|---|---|---|
| Settlement period | BRS-021 time series period | Same period boundaries |
| Metering point GSRN | DataHub GSRN | 18-digit standard identifier |
| Grid tariffs | BRS-031/BRS-034 price data | Received from grid companies via DataHub |
| Settlement status lifecycle | Not in DataHub | WattsOn-internal invoicing management |
| Peppol BIS document format | Industry standard | Compatible with Danish e-invoicing requirements |

---

## Summary

You just verified the complete invoicing handoff:

1. ✅ Settlement exists with full line breakdown
2. ✅ Peppol BIS-aligned document with VAT, parties, and charge IDs
3. ✅ "Bekræft fakturering" button triggers the confirm flow
4. ✅ Status transitions from Calculated → Invoiced
5. ✅ Invoice reference and timestamp stored
6. ✅ Invoiced settlements filtered out of "ready" list
7. ✅ Double-confirm prevented (409 Conflict)
8. ✅ API contract ready for external system integration

**Next step:** With the settlement now marked as "Invoiced", you can proceed to **Scenario 003 (Correction Detection)** to see what happens when DataHub sends updated metered data for this already-invoiced period.
