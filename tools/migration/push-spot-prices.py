#!/usr/bin/env python3
"""
Push cached spot prices to WattsOn API.
Usage: python3 push-spot-prices.py [--cache cache/spot-prices-dk1.json] [--url http://localhost:5100]

Converts from DKK/MWh (Energi Data Service) to DKK/kWh (WattsOn).
Sends in batches to avoid timeout/memory issues.
"""

import argparse
import json
import requests
import sys

BATCH_SIZE = 2000  # hours per API call (reduced from 5000 to avoid OOM in WSL2)

def main():
    parser = argparse.ArgumentParser(description="Push spot prices to WattsOn")
    parser.add_argument("--cache", default="cache/spot-prices-dk1.json", help="Cache file")
    parser.add_argument("--url", default="http://localhost:5100", help="WattsOn API URL")
    parser.add_argument("--area", default=None, help="Override price area (default: from cache)")
    args = parser.parse_args()

    with open(args.cache) as f:
        cache = json.load(f)

    area = args.area or cache.get("area", "DK1")
    prices = cache["prices"]
    
    # Convert DKK/MWh → DKK/kWh (÷1000)
    points = []
    skipped = 0
    for p in prices:
        if p["spotPriceDkk"] is None:
            skipped += 1
            continue
        points.append({
            "timestamp": p["hourUtc"],
            "priceDkkPerKwh": round(p["spotPriceDkk"] / 1000, 8),
        })
    
    print(f"Pushing {len(points)} {area} spot prices ({skipped} skipped, null price)")
    print(f"  Range: {points[0]['timestamp'][:10]} → {points[-1]['timestamp'][:10]}")
    print(f"  Price: {min(p['priceDkkPerKwh'] for p in points):.6f} → {max(p['priceDkkPerKwh'] for p in points):.6f} DKK/kWh")
    
    total_inserted = 0
    total_updated = 0
    
    for i in range(0, len(points), BATCH_SIZE):
        batch = points[i:i+BATCH_SIZE]
        r = requests.post(f"{args.url}/api/spot-prices", json={
            "priceArea": area,
            "points": batch,
        }, timeout=60)
        
        if r.status_code != 200:
            print(f"  ERROR batch {i//BATCH_SIZE + 1}: {r.status_code} {r.text[:200]}")
            sys.exit(1)
        
        result = r.json()
        total_inserted += result.get("inserted", 0)
        total_updated += result.get("updated", 0)
        
        end_idx = min(i + BATCH_SIZE, len(points))
        print(f"  Batch {i//BATCH_SIZE + 1}: {batch[0]['timestamp'][:10]} → {batch[-1]['timestamp'][:10]} "
              f"({result.get('inserted', 0)} new, {result.get('updated', 0)} updated)")
    
    print(f"\n✅ Done: {total_inserted} inserted, {total_updated} updated")

if __name__ == "__main__":
    main()
