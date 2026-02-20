#!/usr/bin/env python3
"""Generate self-contained interactive HTML settlement provenance report."""

import json
import sys
from pathlib import Path

def main():
    cache_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("./cache/prod-405013.json")
    output_path = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("./cache/reports/settlement-provenance.html")
    
    print(f"Reading {cache_path}...")
    with open(cache_path) as f:
        data = json.load(f)
    
    # Build lean report data
    report_settlements = []
    for s in data["settlements"]:
        hourly = []
        for h in s.get("hourlyLines", []):
            hourly.append([
                h["timestamp"],
                round(h["kwh"], 6),
                round(h["spotPriceDkkPerKwh"], 6),
                round(h["calculatedPriceDkkPerKwh"], 6)
            ])
        
        tariffs = []
        for t in s["tariffLines"]:
            prov = t.get("rateProvenance")
            tariff_obj = {
                "id": t["partyChargeTypeId"],
                "desc": t["description"],
                "amount": round(t["amountDkk"], 4),
                "energy": round(t["energyKwh"], 4),
                "avg": round(t["avgUnitPrice"], 6)
            }
            if prov:
                tariff_obj["prov"] = {
                    "startDate": prov["rateStartDate"],
                    "isHourly": prov["isHourly"],
                    "flat": prov["flatRate"],
                    "hourly": prov.get("hourlyRates"),
                    "candidates": prov["candidateRateCount"],
                    "rule": prov["selectionRule"]
                }
            tariffs.append(tariff_obj)
        
        report_settlements.append({
            "gsrn": s["gsrn"],
            "start": s["periodStart"],
            "end": s["periodEnd"],
            "log": s["billingLogNum"].strip() if isinstance(s["billingLogNum"], str) else s["billingLogNum"],
            "key": s["histKeyNumber"].strip() if isinstance(s["histKeyNumber"], str) else s["histKeyNumber"],
            "kwh": round(s["totalEnergyKwh"], 4),
            "elec": round(s["electricityAmountDkk"], 4),
            "spot": round(s["spotAmountDkk"], 4),
            "margin": round(s["marginAmountDkk"], 4),
            "total": round(s["totalAmountDkk"], 4),
            "product": s.get("productName"),
            "marginRate": s.get("marginRateDkkPerKwh"),
            "hours": hourly,
            "tariffs": tariffs
        })
    
    report_settlements.sort(key=lambda s: s["start"])
    
    report_data = {
        "extractedAt": data.get("extractedAt"),
        "accounts": data.get("accountNumbers", []),
        "supplierGln": data.get("supplierGln"),
        "supplierName": data.get("supplierName"),
        "customer": data["customers"][0]["name"] if data.get("customers") else "Unknown",
        "settlements": report_settlements
    }
    
    report_json = json.dumps(report_data, separators=(',', ':'))
    print(f"Report data: {len(report_json) / 1024 / 1024:.1f} MB, {len(report_settlements)} settlements")
    
    html = HTML_TEMPLATE.replace("__REPORT_DATA__", report_json)
    
    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(html)
    
    print(f"Report: {output_path.resolve()} ({len(html) / 1024 / 1024:.1f} MB)")


HTML_TEMPLATE = r'''<!DOCTYPE html>
<html lang="da">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>WattsOn Settlement Provenance Report</title>
<style>
:root {
  --bg: #f8fafc; --card: #fff; --border: #e2e8f0; --text: #1e293b;
  --text2: #64748b; --accent: #0d9488; --accent2: #14b8a6;
  --warn: #f59e0b; --err: #ef4444; --ok: #22c55e;
  --purple: #8b5cf6; --blue: #3b82f6;
}
* { margin: 0; padding: 0; box-sizing: border-box; }
body { font-family: Inter, -apple-system, sans-serif; background: var(--bg); color: var(--text); line-height: 1.5; }
.container { max-width: 1200px; margin: 0 auto; padding: 24px; }
h1 { font-size: 1.5rem; font-weight: 700; margin-bottom: 4px; }
.subtitle { color: var(--text2); font-size: 0.875rem; margin-bottom: 24px; }
.meta { display: flex; gap: 24px; flex-wrap: wrap; margin-bottom: 24px; }
.meta-item { font-size: 0.8rem; color: var(--text2); }
.meta-item strong { color: var(--text); }
.picker { display: flex; gap: 12px; align-items: center; margin-bottom: 24px; flex-wrap: wrap; }
.picker select { padding: 8px 12px; border: 1px solid var(--border); border-radius: 8px; font-size: 0.9rem; min-width: 400px; background: var(--card); }
.picker .nav { display: flex; gap: 4px; }
.picker button { padding: 6px 14px; border: 1px solid var(--border); border-radius: 6px; background: var(--card); cursor: pointer; font-size: 0.85rem; }
.picker button:hover { background: var(--accent); color: white; border-color: var(--accent); }
.card { background: var(--card); border: 1px solid var(--border); border-radius: 12px; padding: 20px; margin-bottom: 16px; }
.card-title { font-size: 0.75rem; text-transform: uppercase; letter-spacing: 0.05em; color: var(--text2); margin-bottom: 12px; font-weight: 600; }
.stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 16px; }
.stat { text-align: center; }
.stat .val { font-size: 1.4rem; font-weight: 700; font-variant-numeric: tabular-nums; }
.stat .lbl { font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.05em; color: var(--text2); }
.val.spot { color: var(--blue); }
.val.margin { color: var(--purple); }
.val.tariff { color: var(--accent); }
.val.total { color: var(--text); }
.val.warn { color: var(--err); }
.breakdown { margin-top: 16px; }
.component { display: grid; grid-template-columns: 1fr 120px 120px; gap: 8px; padding: 10px 0; border-bottom: 1px solid var(--border); align-items: center; }
.component:last-child { border: none; }
.comp-name { font-weight: 500; font-size: 0.9rem; }
.comp-name small { color: var(--text2); font-weight: 400; }
.comp-amount { text-align: right; font-variant-numeric: tabular-nums; font-weight: 600; }
.comp-pct { text-align: right; font-variant-numeric: tabular-nums; color: var(--text2); font-size: 0.85rem; }
.prov { margin-top: 8px; padding: 12px 16px; background: #f1f5f9; border-radius: 8px; font-size: 0.8rem; }
.prov .row { display: flex; gap: 8px; margin-bottom: 4px; }
.prov .label { color: var(--text2); min-width: 140px; flex-shrink: 0; }
.prov .value { font-family: "JetBrains Mono", "Fira Code", monospace; font-weight: 500; word-break: break-all; }
.prov .rule { margin-top: 8px; padding: 8px; background: #fff; border-radius: 4px; font-style: italic; color: var(--text2); }
.flag { display: inline-block; padding: 2px 8px; border-radius: 4px; font-size: 0.7rem; font-weight: 600; margin-left: 6px; }
.flag.warn { background: #fef3c7; color: #92400e; }
.flag.ok { background: #dcfce7; color: #166534; }
details { margin-top: 8px; }
summary { cursor: pointer; font-size: 0.85rem; color: var(--accent); font-weight: 500; padding: 4px 0; }
summary:hover { color: var(--accent2); }
.tbl { width: 100%; border-collapse: collapse; font-size: 0.8rem; margin-top: 8px; }
.tbl th { text-align: left; padding: 6px 8px; border-bottom: 2px solid var(--border); font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text2); position: sticky; top: 0; background: var(--card); }
.tbl td { padding: 5px 8px; border-bottom: 1px solid #f1f5f9; font-variant-numeric: tabular-nums; }
.tbl tr:hover { background: #f8fafc; }
.tbl .r { text-align: right; }
.tbl .mono { font-family: "JetBrains Mono", "Fira Code", monospace; font-size: 0.75rem; }
.source { display: inline-flex; align-items: center; gap: 6px; padding: 3px 10px; background: #eff6ff; border-radius: 4px; font-size: 0.75rem; font-family: monospace; color: var(--blue); margin: 4px 0; }
.chart-bar { display: flex; height: 28px; border-radius: 6px; overflow: hidden; margin: 8px 0; }
.chart-bar .seg { display: flex; align-items: center; justify-content: center; font-size: 0.7rem; color: white; font-weight: 600; min-width: 2px; transition: all .2s; }
.seg.spot { background: var(--blue); }
.seg.margin { background: var(--purple); }
.seg.tariff { background: var(--accent); }
.detail-wrap { max-height: 500px; overflow-y: auto; }
#detail { min-height: 400px; }
.empty { padding: 60px; text-align: center; color: var(--text2); }
</style>
</head>
<body>
<div class="container">
  <h1>⚡ Settlement Provenance Report</h1>
  <p class="subtitle">Traces every number back to its Xellent source table and row</p>
  <div class="meta" id="meta"></div>
  <div class="picker">
    <select id="sel" onchange="render()"></select>
    <div class="nav">
      <button onclick="nav(-1)">◀ Forrige</button>
      <button onclick="nav(1)">Næste ▶</button>
    </div>
  </div>
  <div id="detail"><div class="empty">Vælg en afregning ovenfor</div></div>
</div>
<script>
const DATA = __REPORT_DATA__;
const S = DATA.settlements;

const fmt = (v, d) => v == null ? '—' : v.toLocaleString('da-DK', {minimumFractionDigits: d||2, maximumFractionDigits: d||2});
const fmtK = v => fmt(v, 4);
const fmtP = v => fmt(v, 6);
const pct = (v, t) => t === 0 ? '0%' : (v/t*100).toFixed(1) + '%';
const ts = s => s ? new Date(s).toLocaleDateString('da-DK', {year:'numeric',month:'short',day:'numeric'}) : '—';
const tsH = s => s ? new Date(s).toLocaleString('da-DK', {month:'short',day:'numeric',hour:'2-digit',minute:'2-digit'}) : '';
const esc = s => String(s).replace(/</g,'&lt;').replace(/>/g,'&gt;');

// Meta
document.getElementById('meta').innerHTML =
  '<span class="meta-item">Kunde: <strong>'+esc(DATA.customer)+'</strong></span>' +
  '<span class="meta-item">GLN: <strong>'+esc(DATA.supplierGln)+'</strong></span>' +
  '<span class="meta-item">Leverandør: <strong>'+esc(DATA.supplierName)+'</strong></span>' +
  '<span class="meta-item">Afregninger: <strong>'+S.length+'</strong></span>' +
  '<span class="meta-item">Udtræk: <strong>'+ts(DATA.extractedAt)+'</strong></span>';

// Populate dropdown
var sel = document.getElementById('sel');
S.forEach(function(s, i) {
  var o = document.createElement('option');
  o.value = i;
  o.textContent = ts(s.start) + ' → ' + ts(s.end) + '  |  ' + fmt(s.kwh,1) + ' kWh  |  ' + fmt(s.total) + ' DKK  [HistKey ' + s.key + ']';
  sel.appendChild(o);
});

function nav(dir) {
  var i = parseInt(sel.value) + dir;
  if (i >= 0 && i < S.length) { sel.value = i; render(); }
}

function render() {
  var s = S[parseInt(sel.value)];
  if (!s) return;
  
  var tariffTotal = s.tariffs.reduce(function(a,t){return a+t.amount;}, 0);
  var absTotal = Math.abs(s.spot) + Math.abs(s.margin) + Math.abs(tariffTotal);
  
  // Detect warnings
  var flags = [];
  s.tariffs.forEach(function(t) {
    if (t.avg > 5) flags.push('⚠️ ' + t.desc + ': avg rate ' + fmtP(t.avg) + ' DKK/kWh seems too high (subscription amount?)');
    if (t.prov && t.prov.candidates > 100) flags.push('⚠️ ' + t.desc + ': ' + t.prov.candidates + ' candidate rate rows — may be picking wrong rate');
  });
  
  var h = '';
  
  // Overview card
  h += '<div class="card"><div class="card-title">Afregning</div>';
  h += '<div class="stats">';
  h += '<div class="stat"><div class="val">'+ts(s.start)+'</div><div class="lbl">Periode start</div></div>';
  h += '<div class="stat"><div class="val">'+ts(s.end)+'</div><div class="lbl">Periode slut</div></div>';
  h += '<div class="stat"><div class="val">'+fmt(s.kwh,2)+'</div><div class="lbl">kWh</div></div>';
  h += '<div class="stat"><div class="val">'+s.hours.length+'</div><div class="lbl">Timer</div></div>';
  h += '<div class="stat"><div class="val total">'+fmt(s.total)+'</div><div class="lbl">Total DKK</div></div>';
  h += '</div>';
  h += '<div class="source">📋 FlexBillingHistoryTable · HistKeyNumber = '+esc(s.key)+' · BillingLogNum = '+esc(s.log)+'</div>';
  h += '</div>';
  
  // Warnings
  if (flags.length > 0) {
    h += '<div class="card" style="border-color:var(--err)"><div class="card-title" style="color:var(--err)">⚠️ Advarsler</div>';
    flags.forEach(function(f) { h += '<div style="padding:4px 0;font-size:0.85rem;">'+esc(f)+'</div>'; });
    h += '</div>';
  }
  
  // Component breakdown
  h += '<div class="card"><div class="card-title">Komponentopdeling</div>';
  h += '<div class="chart-bar">';
  if (absTotal > 0) {
    h += '<div class="seg spot" style="width:'+pct(Math.abs(s.spot),absTotal)+'" title="Spot: '+fmt(s.spot)+' DKK">Spot</div>';
    h += '<div class="seg margin" style="width:'+pct(Math.abs(s.margin),absTotal)+'" title="Margin: '+fmt(s.margin)+' DKK">Margin</div>';
    h += '<div class="seg tariff" style="width:'+pct(Math.abs(tariffTotal),absTotal)+'" title="Tariffer: '+fmt(tariffTotal)+' DKK">Tariffer</div>';
  }
  h += '</div>';
  h += '<div class="breakdown">';
  h += '<div class="component"><div class="comp-name">Spot <small>Σ kWh × PowerExchangePrice</small></div><div class="comp-amount val spot">'+fmt(s.spot)+'</div><div class="comp-pct">'+pct(Math.abs(s.spot),Math.abs(s.total))+'</div></div>';
  h += '<div class="component"><div class="comp-name">Margin <small>Σ kWh × (CalculatedPrice − SpotPrice)</small></div><div class="comp-amount val margin">'+fmt(s.margin)+'</div><div class="comp-pct">'+pct(Math.abs(s.margin),Math.abs(s.total))+'</div></div>';
  s.tariffs.forEach(function(t) {
    var isWarn = t.avg > 5;
    h += '<div class="component"><div class="comp-name">'+esc(t.desc)+' <small>['+esc(t.id)+']</small>';
    if (isWarn) h += ' <span class="flag warn">⚠ TJEK</span>';
    h += '</div><div class="comp-amount val '+(isWarn?'warn':'tariff')+'">'+fmt(t.amount)+'</div><div class="comp-pct">'+pct(Math.abs(t.amount),Math.abs(s.total))+'</div></div>';
  });
  h += '<div class="component" style="border-top:2px solid var(--border);padding-top:12px"><div class="comp-name" style="font-weight:700">Total</div><div class="comp-amount val total" style="font-size:1.1rem">'+fmt(s.total)+'</div><div class="comp-pct">100%</div></div>';
  h += '</div></div>';
  
  // Tariff provenance cards
  s.tariffs.forEach(function(t) {
    var p = t.prov;
    var isWarn = t.avg > 5;
    h += '<div class="card"'+(isWarn?' style="border-color:var(--err)"':'')+'>';
    h += '<div class="card-title">Tarif: '+esc(t.desc)+' ['+esc(t.id)+']</div>';
    h += '<div class="stats" style="margin-bottom:12px">';
    h += '<div class="stat"><div class="val '+(isWarn?'warn':'tariff')+'">'+fmt(t.amount)+'</div><div class="lbl">Beløb DKK</div></div>';
    h += '<div class="stat"><div class="val">'+fmt(t.energy,2)+'</div><div class="lbl">kWh</div></div>';
    h += '<div class="stat"><div class="val '+(isWarn?'warn':'')+'">'+fmtP(t.avg)+'</div><div class="lbl">Gns. DKK/kWh</div></div>';
    h += '</div>';
    
    if (p) {
      h += '<div class="prov">';
      h += '<div class="row"><span class="label">Kildetabel:</span><span class="value">EXU_PRICEELEMENTRATES</span></div>';
      h += '<div class="row"><span class="label">PartyChargeTypeId:</span><span class="value">'+esc(t.id)+'</span></div>';
      h += '<div class="row"><span class="label">Valgt takst startdato:</span><span class="value">'+ts(p.startDate)+'</span></div>';
      h += '<div class="row"><span class="label">Taksttype:</span><span class="value">'+(p.isHourly ? 'Timedifferentieret (Price1..24)' : 'Flad takst (Price-kolonnen)')+'</span></div>';
      h += '<div class="row"><span class="label">Flad takst værdi:</span><span class="value">'+fmtP(p.flat)+' DKK/kWh';
      if (p.flat > 5) h += ' <span class="flag warn">MISTÆNKELIG — abonnementsbeløb?</span>';
      h += '</span></div>';
      if (p.hourly) {
        h += '<div class="row"><span class="label">Time-takster:</span><span class="value">['+p.hourly.map(function(r){return fmtP(r);}).join(', ')+']</span></div>';
      }
      h += '<div class="row"><span class="label">Kandidatrækker:</span><span class="value">'+p.candidates+' rækker med StartDate ≤ periodestart';
      if (p.candidates > 50) h += ' <span class="flag warn">HØJT — takst-tabel kan være per-GSRN</span>';
      h += '</span></div>';
      h += '<div class="rule">'+esc(p.rule)+'</div>';
      h += '</div>';
    }
    
    // Expandable hourly tariff detail (recomputed from hours + rate)
    h += '<details><summary>Vis beregning pr. time ('+s.hours.length+' rækker)</summary>';
    h += '<div class="detail-wrap"><table class="tbl"><thead><tr><th>Tidspunkt</th><th class="r">kWh</th><th class="r">Takst DKK/kWh</th><th class="r">Beløb DKK</th></tr></thead><tbody>';
    var runTotal = 0;
    s.hours.forEach(function(hr) {
      var kwh = hr[1];
      var rate;
      if (p && p.isHourly && p.hourly) {
        var hourIdx = new Date(hr[0]).getUTCHours();
        rate = p.hourly[hourIdx] > 0 ? p.hourly[hourIdx] : p.flat;
      } else {
        rate = p ? p.flat : t.avg;
      }
      var amt = kwh * rate;
      runTotal += amt;
      h += '<tr><td class="mono">'+tsH(hr[0])+'</td><td class="r">'+fmtK(kwh)+'</td><td class="r">'+fmtP(rate)+'</td><td class="r">'+fmtK(amt)+'</td></tr>';
    });
    var diff = Math.abs(runTotal - t.amount);
    h += '<tr style="font-weight:700;border-top:2px solid var(--border)"><td>Total (genberegnet)</td><td></td><td></td><td class="r">'+fmt(runTotal,4);
    h += diff > 0.01 ? ' <span class="flag warn">≠ '+fmt(t.amount,4)+'</span>' : ' <span class="flag ok">✓</span>';
    h += '</td></tr>';
    h += '</tbody></table></div></details>';
    h += '</div>';
  });
  
  // Hourly consumption
  h += '<div class="card"><div class="card-title">Timeforbrugsdata (FlexBillingHistoryLine)</div>';
  h += '<div class="source">📋 FlexBillingHistoryLine · HistKeyNumber = '+esc(s.key)+' · '+s.hours.length+' rækker</div>';
  h += '<details><summary>Vis alle '+s.hours.length+' timerækker</summary>';
  h += '<div class="detail-wrap"><table class="tbl"><thead><tr><th>Tidspunkt</th><th class="r">kWh</th><th class="r">Spot DKK/kWh</th><th class="r">Beregnet DKK/kWh</th><th class="r">Margin DKK/kWh</th><th class="r">Spot DKK</th><th class="r">Margin DKK</th></tr></thead><tbody>';
  var tKwh=0, tSpot=0, tMarg=0;
  s.hours.forEach(function(hr) {
    var kwh=hr[1], spot=hr[2], calc=hr[3], marg=calc-spot;
    var sAmt=kwh*spot, mAmt=kwh*marg;
    tKwh+=kwh; tSpot+=sAmt; tMarg+=mAmt;
    h += '<tr><td class="mono">'+tsH(hr[0])+'</td><td class="r">'+fmtK(kwh)+'</td><td class="r">'+fmtP(spot)+'</td><td class="r">'+fmtP(calc)+'</td><td class="r">'+fmtP(marg)+'</td><td class="r">'+fmtK(sAmt)+'</td><td class="r">'+fmtK(mAmt)+'</td></tr>';
  });
  h += '<tr style="font-weight:700;border-top:2px solid var(--border)"><td>Total</td><td class="r">'+fmt(tKwh,4)+'</td><td></td><td></td><td></td><td class="r">'+fmt(tSpot,4)+'</td><td class="r">'+fmt(tMarg,4)+'</td></tr>';
  h += '</tbody></table></div></details></div>';
  
  document.getElementById('detail').innerHTML = h;
}

sel.value = 0;
render();
</script>
</body>
</html>'''


if __name__ == "__main__":
    main()
