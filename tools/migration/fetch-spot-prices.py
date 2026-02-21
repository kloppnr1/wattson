#!/usr/bin/env python3
"""
Fetch historical spot prices from Energi Data Service and cache locally.
Usage: python3 fetch-spot-prices.py [--area DK1] [--from 2020-01-01] [--to 2025-10-02] [--cache cache/spot-prices.json]

Fetches in batches of 10,000 records, writes to a JSON cache file.
Re-running with an existing cache file will only fetch missing date ranges.
"""

import argparse
import json
import os
import sys
import time
import urllib.request
import urllib.parse
from datetime import datetime, timedelta

API_BASE = "https://api.energidataservice.dk/dataset/Elspotprices"
BATCH_SIZE = 10000

def fetch_batch(area: str, start: str, end: str, offset: int) -> dict:
    """Fetch a batch of spot prices from Energi Data Service."""
    params = {
        "offset": offset,
        "limit": BATCH_SIZE,
        "start": start,
        "end": end,
        "filter": json.dumps({"PriceArea": [area]}),
        "sort": "HourUTC ASC",
    }
    url = f"{API_BASE}?{urllib.parse.urlencode(params)}"
    
    req = urllib.request.Request(url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read())

def main():
    parser = argparse.ArgumentParser(description="Fetch historical spot prices")
    parser.add_argument("--area", default="DK1", help="Price area (DK1 or DK2)")
    parser.add_argument("--from", dest="from_date", default="2020-01-01", help="Start date (YYYY-MM-DD)")
    parser.add_argument("--to", dest="to_date", default="2025-10-02", help="End date (YYYY-MM-DD)")
    parser.add_argument("--cache", default="cache/spot-prices.json", help="Cache file path")
    args = parser.parse_args()

    # Load existing cache
    cache = {"area": args.area, "fromDate": args.from_date, "toDate": args.to_date, "prices": []}
    if os.path.exists(args.cache):
        with open(args.cache) as f:
            cache = json.load(f)
        print(f"Loaded existing cache: {len(cache['prices'])} prices ({cache['fromDate']} → {cache['toDate']})")

    # Build set of existing hours for dedup
    existing_hours = {p["hourUtc"] for p in cache["prices"]}
    
    # Fetch
    offset = 0
    new_count = 0
    total = None
    
    print(f"Fetching {args.area} spot prices: {args.from_date} → {args.to_date}")
    
    while True:
        try:
            data = fetch_batch(args.area, args.from_date, args.to_date, offset)
        except Exception as e:
            print(f"  Error at offset {offset}: {e}")
            time.sleep(2)
            continue
            
        if total is None:
            total = data["total"]
            print(f"  Total records available: {total}")
        
        records = data.get("records", [])
        if not records:
            break
            
        for r in records:
            hour_utc = r["HourUTC"]
            if hour_utc not in existing_hours:
                cache["prices"].append({
                    "hourUtc": hour_utc,
                    "hourDk": r["HourDK"],
                    "area": r["PriceArea"],
                    "spotPriceDkk": r["SpotPriceDKK"],  # DKK per MWh
                    "spotPriceEur": r["SpotPriceEUR"],
                })
                existing_hours.add(hour_utc)
                new_count += 1
        
        offset += len(records)
        pct = min(100, offset * 100 / total) if total else 0
        print(f"  Fetched {offset}/{total} ({pct:.0f}%) — {new_count} new", end="\r")
        
        if len(records) < BATCH_SIZE:
            break
            
        time.sleep(0.5)  # Be nice to the API
    
    # Sort by hour
    cache["prices"].sort(key=lambda p: p["hourUtc"])
    cache["area"] = args.area
    cache["fromDate"] = args.from_date
    cache["toDate"] = args.to_date
    cache["fetchedAt"] = datetime.utcnow().isoformat() + "Z"
    cache["count"] = len(cache["prices"])
    
    # Summary stats
    if cache["prices"]:
        prices_dkk = [p["spotPriceDkk"] for p in cache["prices"] if p["spotPriceDkk"] is not None]
        cache["stats"] = {
            "min": min(prices_dkk),
            "max": max(prices_dkk),
            "avg": sum(prices_dkk) / len(prices_dkk),
            "first": cache["prices"][0]["hourUtc"],
            "last": cache["prices"][-1]["hourUtc"],
        }
    
    # Write cache
    os.makedirs(os.path.dirname(args.cache), exist_ok=True)
    with open(args.cache, "w") as f:
        json.dump(cache, f, indent=2)
    
    print(f"\n✅ Cache written: {args.cache}")
    print(f"   {len(cache['prices'])} total prices ({cache['prices'][0]['hourUtc'][:10]} → {cache['prices'][-1]['hourUtc'][:10]})")
    if "stats" in cache:
        s = cache["stats"]
        print(f"   Price range: {s['min']:.2f} → {s['max']:.2f} DKK/MWh (avg {s['avg']:.2f})")

if __name__ == "__main__":
    main()
