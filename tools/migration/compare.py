#!/usr/bin/env python3
"""Compare WattsOn extraction (prod-*.json) vs Xellent reference (xellent-*.json)."""

import json
import sys
from pathlib import Path

def main():
    wattson_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("./cache/prod-405013.json")
    xellent_path = Path(sys.argv[2]) if len(sys.argv) > 2 else Path("./cache/reports/xellent-405013.json")
    
    print(f"WattsOn: {wattson_path}")
    print(f"Xellent: {xellent_path}")
    
    with open(wattson_path) as f:
        w_data = json.load(f)
    with open(xellent_path) as f:
        x_data = json.load(f)
    
    # Index settlements by histKeyNumber
    w_by_key = {}
    for s in w_data.get("settlements", []):
        key = s.get("histKeyNumber", "").strip()
        w_by_key[key] = s
    
    x_by_key = {}
    for s in x_data.get("settlements", []):
        key = s.get("histKeyNumber", "").strip()
        x_by_key[key] = s
    
    all_keys = sorted(set(w_by_key.keys()) | set(x_by_key.keys()))
    
    print(f"\nWattsOn settlements: {len(w_by_key)}")
    print(f"Xellent settlements: {len(x_by_key)}")
    print(f"Common keys: {len(set(w_by_key.keys()) & set(x_by_key.keys()))}")
    print(f"Only in WattsOn: {len(set(w_by_key.keys()) - set(x_by_key.keys()))}")
    print(f"Only in Xellent: {len(set(x_by_key.keys()) - set(w_by_key.keys()))}")
    
    print("\n" + "="*120)
    print(f"{'Key':>12}  {'Period':>22}  {'W kWh':>10}  {'X kWh':>10}  {'ΔkWh':>8}  {'W Spot':>10}  {'X Spot':>10}  {'ΔSpot':>8}  {'W Margin':>10}  {'X Margin':>10}  {'ΔMargin':>8}  {'W Total':>10}  {'X Total':>10}  {'ΔTotal':>10}  {'Tariff Diffs'}")
    print("="*120)
    
    total_diffs = 0
    total_amount_diff = 0
    exact_matches = 0
    tariff_diffs_total = 0
    
    for key in all_keys:
        ws = w_by_key.get(key)
        xs = x_by_key.get(key)
        
        if not ws:
            print(f"{key:>12}  MISSING in WattsOn")
            total_diffs += 1
            continue
        if not xs:
            print(f"{key:>12}  MISSING in Xellent")
            total_diffs += 1
            continue
        
        period = xs.get("periodStart", "")[:10]
        
        w_kwh = ws.get("totalEnergyKwh", 0)
        x_kwh = xs.get("totalEnergyKwh", 0)
        d_kwh = w_kwh - x_kwh
        
        w_spot = ws.get("spotAmountDkk", 0)
        x_spot = xs.get("spotAmountDkk", 0)
        d_spot = w_spot - x_spot
        
        w_margin = ws.get("marginAmountDkk", 0)
        x_margin = xs.get("marginAmountDkk", 0)
        d_margin = w_margin - x_margin
        
        w_total = ws.get("totalAmountDkk", 0)
        x_total = xs.get("totalAmountDkk", 0)
        d_total = w_total - x_total
        
        # Compare tariff lines
        w_tariffs = {t["partyChargeTypeId"]: t for t in ws.get("tariffLines", [])}
        x_tariffs = {t["partyChargeTypeId"]: t for t in xs.get("tariffLines", [])}
        
        all_tariff_ids = sorted(set(w_tariffs.keys()) | set(x_tariffs.keys()))
        tariff_diff_notes = []
        
        for tid in all_tariff_ids:
            wt = w_tariffs.get(tid)
            xt = x_tariffs.get(tid)
            
            if not wt:
                tariff_diff_notes.append(f"+X:{tid}={xt['amountDkk']:.2f}")
                tariff_diffs_total += 1
            elif not xt:
                tariff_diff_notes.append(f"+W:{tid}={wt['amountDkk']:.2f}")
                tariff_diffs_total += 1
            else:
                diff = abs(wt["amountDkk"] - xt["amountDkk"])
                if diff > 0.01:
                    tariff_diff_notes.append(f"Δ{tid}={wt['amountDkk']-xt['amountDkk']:+.2f}")
                    tariff_diffs_total += 1
        
        has_diff = abs(d_kwh) > 0.001 or abs(d_spot) > 0.01 or abs(d_margin) > 0.01 or abs(d_total) > 0.01 or len(tariff_diff_notes) > 0
        
        if has_diff:
            total_diffs += 1
            total_amount_diff += d_total
            tariff_str = "; ".join(tariff_diff_notes[:5])
            if len(tariff_diff_notes) > 5:
                tariff_str += f" +{len(tariff_diff_notes)-5} more"
            print(f"{key:>12}  {period:>22}  {w_kwh:>10.2f}  {x_kwh:>10.2f}  {d_kwh:>+8.2f}  {w_spot:>10.2f}  {x_spot:>10.2f}  {d_spot:>+8.2f}  {w_margin:>10.2f}  {x_margin:>10.2f}  {d_margin:>+8.2f}  {w_total:>10.2f}  {x_total:>10.2f}  {d_total:>+10.2f}  {tariff_str}")
        else:
            exact_matches += 1
    
    print("="*120)
    print(f"\nSUMMARY:")
    print(f"  Total settlements compared: {len(all_keys)}")
    print(f"  Exact matches: {exact_matches}")
    print(f"  Settlements with diffs: {total_diffs}")
    print(f"  Individual tariff diffs: {tariff_diffs_total}")
    print(f"  Total amount difference: {total_amount_diff:+.2f} DKK")

if __name__ == "__main__":
    main()
