using System.Text;
using System.Text.Json;
using WattsOn.Migration.Core.Models;

namespace WattsOn.Migration.Cli;

public static class HtmlReportGenerator
{
    public static string Generate(ExtractedData data)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        // Serialize settlements with provenance for embedding
        var settlementsJson = JsonSerializer.Serialize(data.Settlements, jsonOptions);
        var pricesJson = JsonSerializer.Serialize(data.Prices, jsonOptions);
        var productsJson = JsonSerializer.Serialize(data.Products, jsonOptions);
        var customersJson = JsonSerializer.Serialize(data.Customers, jsonOptions);

        return $$"""
<!DOCTYPE html>
<html lang="da">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>WattsOn Settlement Provenance — {{data.AccountNumbers.FirstOrDefault() ?? "unknown"}}</title>
<style>
  :root {
    --bg: #f8fafc; --surface: #ffffff; --border: #e2e8f0;
    --text: #1e293b; --text-muted: #64748b; --text-light: #94a3b8;
    --accent: #0d9488; --accent-light: #ccfbf1; --accent-dark: #115e59;
    --danger: #ef4444; --danger-light: #fef2f2;
    --warning: #f59e0b; --warning-light: #fffbeb;
    --purple: #8b5cf6; --purple-light: #f5f3ff;
    --radius: 8px; --shadow: 0 1px 3px rgba(0,0,0,0.1);
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: 'Inter', -apple-system, sans-serif; background: var(--bg); color: var(--text); line-height: 1.6; }

  .container { max-width: 1200px; margin: 0 auto; padding: 24px; }

  header { background: linear-gradient(135deg, var(--accent-dark), var(--accent)); color: white; padding: 32px 0; margin-bottom: 24px; }
  header .container { display: flex; justify-content: space-between; align-items: center; }
  header h1 { font-size: 1.5rem; font-weight: 700; }
  header .meta { font-size: 0.85rem; opacity: 0.85; text-align: right; }

  .card { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); box-shadow: var(--shadow); margin-bottom: 16px; }
  .card-header { padding: 16px 20px; border-bottom: 1px solid var(--border); display: flex; justify-content: space-between; align-items: center; }
  .card-header h2 { font-size: 1rem; font-weight: 600; }
  .card-body { padding: 20px; }

  .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(160px, 1fr)); gap: 12px; margin-bottom: 20px; }
  .stat { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 16px; text-align: center; }
  .stat-value { font-size: 1.4rem; font-weight: 700; color: var(--accent-dark); font-variant-numeric: tabular-nums; }
  .stat-label { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.05em; color: var(--text-muted); margin-top: 4px; }

  select { padding: 8px 12px; border: 1px solid var(--border); border-radius: var(--radius); font-size: 0.9rem; background: white; min-width: 300px; }

  table { width: 100%; border-collapse: collapse; font-size: 0.85rem; }
  th { text-align: left; padding: 8px 12px; background: var(--bg); border-bottom: 2px solid var(--border); font-weight: 600; font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.03em; color: var(--text-muted); }
  td { padding: 8px 12px; border-bottom: 1px solid var(--border); font-variant-numeric: tabular-nums; }
  tr:hover td { background: #f1f5f9; }
  .text-right { text-align: right; }
  .text-muted { color: var(--text-muted); }
  .text-mono { font-family: 'SF Mono', 'Fira Code', monospace; font-size: 0.8rem; }

  .badge { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 0.7rem; font-weight: 600; text-transform: uppercase; }
  .badge-migrated { background: var(--purple-light); color: var(--purple); }
  .badge-warning { background: var(--warning-light); color: #92400e; }
  .badge-danger { background: var(--danger-light); color: var(--danger); }
  .badge-ok { background: var(--accent-light); color: var(--accent-dark); }

  .provenance { background: #f8fafc; border: 1px solid var(--border); border-radius: var(--radius); padding: 12px 16px; margin: 8px 0; font-size: 0.8rem; }
  .provenance-label { font-weight: 600; color: var(--accent-dark); font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.05em; margin-bottom: 4px; }
  .provenance code { background: #e2e8f0; padding: 1px 6px; border-radius: 3px; font-family: 'SF Mono', monospace; font-size: 0.78rem; }

  .collapsible { cursor: pointer; user-select: none; }
  .collapsible::before { content: '▸ '; color: var(--text-muted); }
  .collapsible.open::before { content: '▾ '; }
  .collapse-content { display: none; }
  .collapse-content.show { display: block; }

  .component-row { display: grid; grid-template-columns: 1fr auto auto; gap: 12px; padding: 12px 0; border-bottom: 1px solid var(--border); align-items: center; }
  .component-row:last-child { border-bottom: none; }
  .component-name { font-weight: 500; }
  .component-amount { font-weight: 600; font-variant-numeric: tabular-nums; text-align: right; min-width: 100px; }
  .component-rate { color: var(--text-muted); font-size: 0.8rem; text-align: right; min-width: 120px; }

  .total-row { display: grid; grid-template-columns: 1fr auto auto; gap: 12px; padding: 16px 0; border-top: 2px solid var(--text); align-items: center; font-weight: 700; font-size: 1.05rem; }

  .anomaly { border-left: 3px solid var(--danger); padding-left: 12px; }

  .section-title { font-size: 0.85rem; font-weight: 600; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.05em; margin: 20px 0 12px; }

  .hourly-scroll { max-height: 400px; overflow-y: auto; border: 1px solid var(--border); border-radius: var(--radius); }

  .chart-bar { height: 4px; background: var(--accent); border-radius: 2px; display: inline-block; vertical-align: middle; }

  .nav-pills { display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; }
  .nav-pill { padding: 6px 14px; border: 1px solid var(--border); border-radius: 20px; font-size: 0.8rem; cursor: pointer; background: white; transition: all 0.15s; }
  .nav-pill:hover { border-color: var(--accent); color: var(--accent); }
  .nav-pill.active { background: var(--accent); color: white; border-color: var(--accent); }

  .tab-content { display: none; }
  .tab-content.active { display: block; }

  @media (max-width: 768px) {
    .stats { grid-template-columns: repeat(2, 1fr); }
    select { min-width: 100%; }
    .component-row { grid-template-columns: 1fr; gap: 4px; }
  }
</style>
</head>
<body>

<header>
  <div class="container">
    <div>
      <h1>⚡ WattsOn Settlement Provenance</h1>
      <div style="opacity:0.85; margin-top:4px">Account {{data.AccountNumbers.FirstOrDefault() ?? "?"}} — {{data.Customers.FirstOrDefault()?.Name ?? "?"}}</div>
    </div>
    <div class="meta">
      <div>Extracted: {{data.ExtractedAt:yyyy-MM-dd HH:mm}} UTC</div>
      <div>Source: AXDB50 (production)</div>
      <div>GSRN: {{data.Customers.FirstOrDefault()?.MeteringPoints.FirstOrDefault()?.Gsrn ?? "?"}}</div>
    </div>
  </div>
</header>

<div class="container">
  <div class="stats">
    <div class="stat"><div class="stat-value" id="stat-settlements">{{data.Settlements.Count}}</div><div class="stat-label">Settlements</div></div>
    <div class="stat"><div class="stat-value">{{data.Prices.Count}}</div><div class="stat-label">DataHub Charges</div></div>
    <div class="stat"><div class="stat-value">{{data.Products.Count}}</div><div class="stat-label">Products</div></div>
    <div class="stat"><div class="stat-value" id="stat-anomalies">—</div><div class="stat-label">Anomalies</div></div>
  </div>

  <div class="card">
    <div class="card-header">
      <h2>Settlement</h2>
      <select id="settlementPicker" onchange="renderSettlement()"></select>
    </div>
    <div class="card-body" id="settlementDetail">
      <p class="text-muted">Select a settlement above.</p>
    </div>
  </div>

  <div class="card">
    <div class="card-header"><h2>Anomaly Scanner</h2></div>
    <div class="card-body" id="anomalyList">
      <p class="text-muted">Scanning…</p>
    </div>
  </div>

  <div class="card">
    <div class="card-header"><h2>DataHub Charges Reference</h2></div>
    <div class="card-body" id="pricesRef"></div>
  </div>
</div>

<script>
const settlements = {{settlementsJson}};
const prices = {{pricesJson}};
const products = {{productsJson}};
const customers = {{customersJson}};

// ─── Helpers ───
const fmt = (n, d=2) => n != null ? Number(n).toFixed(d) : '—';
const fmtDkk = n => n != null ? Number(n).toLocaleString('da-DK', {minimumFractionDigits:2, maximumFractionDigits:2}) + ' DKK' : '—';
const fmtKwh = n => n != null ? Number(n).toLocaleString('da-DK', {minimumFractionDigits:2, maximumFractionDigits:4}) + ' kWh' : '—';
const fmtDate = d => d ? new Date(d).toLocaleDateString('da-DK', {year:'numeric',month:'short',day:'numeric'}) : '—';
const fmtDateTime = d => d ? new Date(d).toLocaleString('da-DK', {month:'short',day:'numeric',hour:'2-digit',minute:'2-digit'}) : '—';

function toggleCollapse(el) {
  el.classList.toggle('open');
  const content = el.nextElementSibling;
  content.classList.toggle('show');
}

// ─── Anomaly Detection ───
function detectAnomalies() {
  const anomalies = [];
  settlements.forEach((s, i) => {
    const hk = (s.histKeyNumber || '').trim();
    // Check tariff rates
    (s.tariffLines || []).forEach(t => {
      if (t.avgUnitPrice > 2.0) {
        anomalies.push({
          severity: 'danger',
          settlement: hk,
          index: i,
          msg: `${t.description} [${t.partyChargeTypeId}]: avg rate ${fmt(t.avgUnitPrice, 4)} DKK/kWh is suspiciously high (> 2 DKK/kWh). Amount: ${fmtDkk(t.amountDkk)}`,
          detail: t.rateProvenance ? `Rate row: StartDate=${fmtDate(t.rateProvenance.rateStartDate)}, FlatRate=${t.rateProvenance.flatRate}, IsHourly=${t.rateProvenance.isHourly}` : null
        });
      }
      if (t.avgUnitPrice < 0) {
        anomalies.push({
          severity: 'warning',
          settlement: hk,
          index: i,
          msg: `${t.description} [${t.partyChargeTypeId}]: negative avg rate ${fmt(t.avgUnitPrice, 4)} DKK/kWh`,
          detail: null
        });
      }
    });
    // Check total vs components
    const componentSum = Number(s.spotAmountDkk||0) + Number(s.marginAmountDkk||0) + (s.tariffLines||[]).reduce((a,t)=>a+Number(t.amountDkk||0),0);
    const diff = Math.abs(componentSum - Number(s.totalAmountDkk||0));
    if (diff > 0.01) {
      anomalies.push({
        severity: 'warning',
        settlement: hk,
        index: i,
        msg: `Total mismatch: components sum to ${fmtDkk(componentSum)} but total is ${fmtDkk(s.totalAmountDkk)} (diff: ${fmt(diff, 4)})`,
        detail: null
      });
    }
    // Check zero energy
    if (Number(s.totalEnergyKwh||0) === 0) {
      anomalies.push({ severity: 'warning', settlement: hk, index: i, msg: 'Zero energy settlement', detail: null });
    }
  });
  return anomalies;
}

// ─── Render Settlement ───
function renderSettlement() {
  const idx = document.getElementById('settlementPicker').value;
  const s = settlements[idx];
  if (!s) return;

  const hk = (s.histKeyNumber||'').trim();
  const bl = (s.billingLogNum||'').trim();
  const hourly = s.hourlyLines || [];
  const maxKwh = Math.max(...hourly.map(h => Number(h.kwh||0)), 0.01);

  let html = `
    <div class="stats" style="margin-bottom:20px">
      <div class="stat"><div class="stat-value">${fmtDate(s.periodStart)}</div><div class="stat-label">Period Start</div></div>
      <div class="stat"><div class="stat-value">${fmtDate(s.periodEnd)}</div><div class="stat-label">Period End</div></div>
      <div class="stat"><div class="stat-value">${fmtKwh(s.totalEnergyKwh)}</div><div class="stat-label">Total Energy</div></div>
      <div class="stat"><div class="stat-value">${fmtDkk(s.totalAmountDkk)}</div><div class="stat-label">Total Amount</div></div>
    </div>

    <div class="provenance">
      <div class="provenance-label">Source Tables</div>
      <code>FLEXBILLINGHISTORYTABLE</code> HistKeyNumber = <code>${hk}</code>, BillingLogNum = <code>${bl}</code><br>
      <code>FLEXBILLINGHISTORYLINE</code> ${hourly.length} hourly rows linked via HistKeyNumber
    </div>

    <div class="section-title">Settlement Components</div>
  `;

  // Spot
  html += `<div class="component-row">
    <div class="component-name">⚡ Spotpris <span class="text-muted" style="font-size:0.8rem">(Σ kWh × PowerExchangePrice per hour)</span></div>
    <div class="component-rate">${fmtKwh(s.totalEnergyKwh)}</div>
    <div class="component-amount">${fmtDkk(s.spotAmountDkk)}</div>
  </div>`;

  // Margin
  html += `<div class="component-row">
    <div class="component-name">📊 Leverandørmargin <span class="text-muted" style="font-size:0.8rem">(Σ kWh × (CalculatedPrice − SpotPrice))</span>
      ${s.productName ? `<br><span class="text-muted">Product: ${s.productName}</span>` : ''}
    </div>
    <div class="component-rate">${fmtKwh(s.totalEnergyKwh)}</div>
    <div class="component-amount">${fmtDkk(s.marginAmountDkk)}</div>
  </div>`;

  // Tariffs
  (s.tariffLines || []).forEach((t, ti) => {
    const isAnomaly = t.avgUnitPrice > 2.0;
    const prov = t.rateProvenance;
    html += `<div class="component-row ${isAnomaly ? 'anomaly' : ''}">
      <div class="component-name">
        🏷️ ${t.description} <span class="text-mono">[${t.partyChargeTypeId}]</span>
        ${isAnomaly ? ' <span class="badge badge-danger">⚠ Suspect rate</span>' : ''}
        <span class="text-muted" style="font-size:0.8rem"> — avg ${fmt(t.avgUnitPrice, 4)} DKK/kWh</span>
      </div>
      <div class="component-rate">${fmtKwh(t.energyKwh)}</div>
      <div class="component-amount">${fmtDkk(t.amountDkk)}</div>
    </div>`;

    if (prov) {
      html += `<div class="provenance" style="margin-left:24px">
        <div class="provenance-label">Rate Lookup Provenance</div>
        Table: <code>${prov.table}</code>, PartyChargeTypeId: <code>${prov.partyChargeTypeId}</code><br>
        Selected rate: StartDate = <code>${fmtDate(prov.rateStartDate)}</code><br>
        Rule: ${prov.selectionRule}<br>
        Flat rate: <code>${prov.flatRate}</code> DKK/kWh | Hourly: <code>${prov.isHourly}</code>
        ${prov.hourlyRates ? `<br>Price1..24: <code>${prov.hourlyRates.map(r=>fmt(r,4)).join(', ')}</code>` : ''}
      </div>`;
    }

    // Collapsible hourly detail
    const detail = t.hourlyDetail || [];
    if (detail.length > 0) {
      html += `<div style="margin-left:24px">
        <div class="collapsible" onclick="toggleCollapse(this)">Show ${detail.length} hourly calculations</div>
        <div class="collapse-content">
          <div class="hourly-scroll"><table>
            <thead><tr><th>Time</th><th>Hour</th><th class="text-right">kWh</th><th class="text-right">Rate (DKK/kWh)</th><th class="text-right">Amount (DKK)</th></tr></thead>
            <tbody>${detail.map(d => `<tr>
              <td>${fmtDateTime(d.timestamp)}</td>
              <td>${d.hour}</td>
              <td class="text-right">${fmt(d.kwh, 4)}</td>
              <td class="text-right">${fmt(d.rateDkkPerKwh, 6)}</td>
              <td class="text-right">${fmt(d.amountDkk, 4)}</td>
            </tr>`).join('')}</tbody>
          </table></div>
        </div>
      </div>`;
    }
  });

  // Total
  html += `<div class="total-row">
    <div>Total</div>
    <div></div>
    <div style="text-align:right">${fmtDkk(s.totalAmountDkk)}</div>
  </div>`;

  // Electricity hourly detail (spot + margin)
  if (hourly.length > 0) {
    html += `<div class="section-title" style="margin-top:24px">Electricity Hourly Detail (FlexBillingHistoryLine)</div>`;
    html += `<div class="collapsible" onclick="toggleCollapse(this)">Show ${hourly.length} hourly consumption lines</div>
    <div class="collapse-content">
      <div class="hourly-scroll"><table>
        <thead><tr>
          <th>Time</th><th class="text-right">kWh</th><th></th>
          <th class="text-right">Spot (DKK/kWh)</th><th class="text-right">Calc (DKK/kWh)</th>
          <th class="text-right">Margin (DKK/kWh)</th>
          <th class="text-right">Spot amt</th><th class="text-right">Margin amt</th>
        </tr></thead>
        <tbody>${hourly.map(h => {
          const barW = Math.round((Number(h.kwh||0) / maxKwh) * 60);
          return `<tr>
            <td>${fmtDateTime(h.timestamp)}</td>
            <td class="text-right">${fmt(h.kwh, 4)}</td>
            <td><span class="chart-bar" style="width:${barW}px"></span></td>
            <td class="text-right">${fmt(h.spotPriceDkkPerKwh, 4)}</td>
            <td class="text-right">${fmt(h.calculatedPriceDkkPerKwh, 4)}</td>
            <td class="text-right">${fmt(Number(h.calculatedPriceDkkPerKwh||0) - Number(h.spotPriceDkkPerKwh||0), 4)}</td>
            <td class="text-right">${fmt(h.spotAmountDkk, 4)}</td>
            <td class="text-right">${fmt(h.marginAmountDkk, 4)}</td>
          </tr>`;
        }).join('')}</tbody>
      </table></div>
    </div>`;
  }

  document.getElementById('settlementDetail').innerHTML = html;
}

// ─── Render Anomalies ───
function renderAnomalies(anomalies) {
  document.getElementById('stat-anomalies').textContent = anomalies.length;
  if (anomalies.length === 0) {
    document.getElementById('anomalyList').innerHTML = '<p style="color:var(--accent)">✅ No anomalies detected.</p>';
    return;
  }
  let html = `<table><thead><tr><th>Severity</th><th>Settlement</th><th>Issue</th></tr></thead><tbody>`;
  anomalies.forEach(a => {
    const badge = a.severity === 'danger' ? 'badge-danger' : 'badge-warning';
    html += `<tr>
      <td><span class="badge ${badge}">${a.severity}</span></td>
      <td><a href="#" onclick="document.getElementById('settlementPicker').value=${a.index};renderSettlement();return false;">${a.settlement}</a></td>
      <td>${a.msg}${a.detail ? `<br><span class="text-muted" style="font-size:0.78rem">${a.detail}</span>` : ''}</td>
    </tr>`;
  });
  html += '</tbody></table>';
  document.getElementById('anomalyList').innerHTML = html;
}

// ─── Render Prices Reference ───
function renderPrices() {
  let html = `<table><thead><tr><th>ChargeId</th><th>Description</th><th>Type</th><th>Category</th><th>Owner GLN</th><th>Resolution</th><th class="text-right"># Points</th></tr></thead><tbody>`;
  prices.sort((a,b) => (a.category||'').localeCompare(b.category||''));
  prices.forEach(p => {
    html += `<tr>
      <td class="text-mono">${p.chargeId}</td>
      <td>${p.description}</td>
      <td>${p.type}</td>
      <td><span class="badge badge-ok">${p.category}</span></td>
      <td class="text-mono">${p.ownerGln}</td>
      <td>${p.resolution || 'flat'}</td>
      <td class="text-right">${(p.points||[]).length}</td>
    </tr>`;
  });
  html += '</tbody></table>';
  document.getElementById('pricesRef').innerHTML = html;
}

// ─── Init ───
const picker = document.getElementById('settlementPicker');
settlements
  .map((s,i) => ({s,i}))
  .sort((a,b) => new Date(a.s.periodStart) - new Date(b.s.periodStart))
  .forEach(({s,i}) => {
    const opt = document.createElement('option');
    opt.value = i;
    const hk = (s.histKeyNumber||'').trim();
    opt.textContent = `${fmtDate(s.periodStart)} → ${fmtDate(s.periodEnd)} | ${fmtKwh(s.totalEnergyKwh)} | HistKey ${hk}`;
    picker.appendChild(opt);
  });

const anomalies = detectAnomalies();
renderAnomalies(anomalies);
renderPrices();
if (settlements.length > 0) {
  picker.value = picker.options[0].value;
  renderSettlement();
}
</script>
</body>
</html>
""";
    }
}
