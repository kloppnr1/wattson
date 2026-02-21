#!/usr/bin/env python3
"""
Validate migrated settlements by cross-checking against cache data.

Three levels of validation:
  1. Internal consistency — sum(hourlyDetail) == amountDkk for each tariff line
  2. Observation match   — hourlyDetail kWh matches time series observations
  3. Independent calcs   — spot price amounts + margin amounts from cache data

Usage:
  python3 recalculate-compare.py --cache cache/prod-405013-v10.json --spot cache/spot-prices-dk1.json [-v]
  python3 recalculate-compare.py --cache cache/prod-405013-v10.json --spot cache/spot-prices-dk1.json --index 45 -v
"""

import argparse, json, sys
from datetime import datetime, timedelta, timezone
from collections import defaultdict


def norm(ts: str) -> str:
    """Normalize timestamp: strip +00:00/Z for comparison."""
    return ts.replace("+00:00", "").replace("Z", "")


def parse_ts(ts: str) -> datetime:
    """Parse ISO timestamp to datetime (UTC)."""
    s = ts.replace("Z", "+00:00")
    return datetime.fromisoformat(s)


def main():
    parser = argparse.ArgumentParser(description="Validate migrated settlements")
    parser.add_argument("--cache", required=True, help="Path to migration cache JSON")
    parser.add_argument("--spot", required=True, help="Path to spot price cache JSON")
    parser.add_argument("-v", "--verbose", action="store_true")
    parser.add_argument("--index", type=int, help="Single settlement index to check")
    parser.add_argument("--deep", action="store_true", help="Show per-hour price analysis for --index")
    args = parser.parse_args()

    data = json.load(open(args.cache))
    spot_cache = json.load(open(args.spot))

    # === Build lookups ===

    # Spot prices: normalized timestamp → DKK/kWh
    spot_lookup = {}
    for p in spot_cache["prices"]:
        if p["spotPriceDkk"] is not None:
            spot_lookup[norm(p["hourUtc"])] = p["spotPriceDkk"] / 1000.0

    # Time series observations: normalized timestamp → kWh
    obs_lookup = {}
    for o in data["timeSeries"][0]["observations"]:
        obs_lookup[norm(o["timestamp"])] = o["kwh"]

    # Products by name
    products = {p["name"]: p for p in data["products"]}

    # Product periods (sorted)
    mp = data["customers"][0]["meteringPoints"][0]
    prod_periods = sorted(mp["productPeriods"], key=lambda pp: pp["start"])

    # Addon product names (not primary products)
    addon_names = {
        "Grøn strøm",
        "Leje af plads Anrenne/Mast/Koncent. El",
        "Refusion af omk. for el-forbrug (el)",
        "ØgetBal.geb.udenf-for",
    }

    # Subscription prices: (chargeId) → sorted list of (norm_ts, monthly_price)
    sub_prices = {}
    for p in data["prices"]:
        if p["type"] == "Abonnement":
            pts = sorted([(norm(pt["timestamp"]), pt["price"]) for pt in p["points"]])
            sub_prices[p["chargeId"]] = pts

    print(f"Cache: {len(data['settlements'])} settlements, {len(obs_lookup)} observations, "
          f"{len(spot_lookup)} spot hours, {len(products)} products")
    obs_ts = sorted(obs_lookup.keys())
    if obs_ts:
        print(f"Obs range: {obs_ts[0][:10]} → {obs_ts[-1][:10]}")
    print()

    # === Helper functions ===

    def find_primary_product(period_start: str):
        """Find the primary (non-addon) product active at period_start."""
        ps = norm(period_start)
        for pp in reversed(prod_periods):
            s = norm(pp["start"])
            e = norm(pp.get("end") or "2099")
            if s <= ps < e and pp["productName"] not in addon_names:
                return products.get(pp["productName"])
        return None

    def find_addons(period_start: str):
        """Find addon products active at period_start."""
        ps = norm(period_start)
        addons = []
        for pp in prod_periods:
            s = norm(pp["start"])
            e = norm(pp.get("end") or "2099")
            if s <= ps < e and pp["productName"] in addon_names:
                prod = products.get(pp["productName"])
                if prod:
                    addons.append(prod)
        return addons

    def margin_rate_at(product, period_start: str) -> float:
        """Get the margin rate (DKK/kWh) for a product at period_start.
        
        Rates with endDate: range match (startDate <= period < endDate)
        Rates without endDate: step-function with strict < (startDate < period)
        This matches Xellent's behavior where a rate 'starting' on a quarter
        boundary applies to periods AFTER that boundary, not AT it.
        """
        ps = norm(period_start)
        rate = 0.0
        for r in sorted(product["rates"], key=lambda x: x["startDate"]):
            rs = norm(r["startDate"])
            end = r.get("endDate")
            if end:
                # Range: startDate <= period < endDate
                if rs <= ps and ps < norm(end):
                    rate = r["rateDkkPerKwh"]
            else:
                # Step function: strict less than
                if rs < ps:
                    rate = r["rateDkkPerKwh"]
        return rate

    def sub_price_at(charge_id: str, period_start: str) -> float:
        """Get subscription monthly price at period_start."""
        ps = norm(period_start)
        pts = sub_prices.get(charge_id, [])
        val = 0.0
        for t, p in pts:
            if t <= ps:
                val = p
            else:
                break
        return val

    # === Validate each settlement ===

    results = []
    for i, s in enumerate(data["settlements"]):
        if args.index is not None and i != args.index:
            continue

        ps_raw = s["periodStart"]
        pe_raw = s["periodEnd"]
        ps = norm(ps_raw)
        pe = norm(pe_raw)
        total_kwh = s["totalEnergyKwh"]
        margin_ref = s["marginAmountDkk"]

        # Find observations for this period
        period_obs = {t: k for t, k in obs_lookup.items() if ps <= t < pe}
        has_obs = len(period_obs) > 0

        # Find product
        prod = find_primary_product(ps_raw)
        prod_name = prod["name"] if prod else "?"
        model = prod["pricingModel"] if prod else "Unknown"
        addons = find_addons(ps_raw)

        # --- Level 1: Internal consistency of tariff lines ---
        consistency_ok = True
        consistency_errors = []
        dh_total_ref = 0.0
        product_total_ref = 0.0

        for t in s["tariffLines"]:
            cid = t["partyChargeTypeId"]
            is_product = cid.startswith("PRODUCT:")

            if is_product:
                product_total_ref += t["amountDkk"]
            else:
                dh_total_ref += t["amountDkk"]

            if t.get("hourlyDetail"):
                hd_sum = sum(h["amountDkk"] for h in t["hourlyDetail"])
                diff = abs(t["amountDkk"] - hd_sum)
                if diff > 0.01:
                    consistency_ok = False
                    consistency_errors.append(
                        f"{cid}: total={t['amountDkk']:.2f} sum(hourly)={hd_sum:.2f} Δ={diff:.4f}"
                    )

        # --- Level 2: Observation matching ---
        obs_matched = 0
        obs_mismatched = 0
        obs_missing = 0
        obs_errors = []

        if has_obs:
            # Use any tariff line with hourlyDetail to check obs
            for t in s["tariffLines"]:
                if t.get("hourlyDetail") and not t["isSubscription"]:
                    for h in t["hourlyDetail"]:
                        hts = norm(h["timestamp"])
                        if hts in period_obs:
                            if abs(h["kwh"] - period_obs[hts]) < 0.005:
                                obs_matched += 1
                            else:
                                obs_mismatched += 1
                                if len(obs_errors) < 3:
                                    obs_errors.append(
                                        f"  {hts}: hd={h['kwh']:.3f} obs={period_obs[hts]:.3f}"
                                    )
                        else:
                            obs_missing += 1
                    break  # Only check against one tariff line's hourlyDetail

        # --- Level 3: Independent calculations ---

        # 3a: Spot price
        recalc_spot = None
        spot_missing_hours = 0
        if has_obs and model == "SpotAddon":
            spot_total = 0.0
            spot_hours = 0
            spot_missing_hours = 0
            for ts, kwh in period_obs.items():
                sp = spot_lookup.get(ts)
                if sp is not None:
                    spot_total += kwh * sp
                    spot_hours += 1
                else:
                    spot_missing_hours += 1
            recalc_spot = spot_total

        # 3b: Main product margin
        recalc_margin = None
        if prod:
            mr = margin_rate_at(prod, ps_raw)
            if has_obs:
                recalc_margin = sum(period_obs.values()) * mr
            else:
                # Use settlement's own total kWh
                recalc_margin = total_kwh * mr

        margin_diff = (recalc_margin - margin_ref) if recalc_margin is not None else None

        # 3c: Addon product amounts
        addon_diffs = []
        for addon in addons:
            addon_name = addon["name"]
            addon_rate = margin_rate_at(addon, ps_raw)
            # Find matching PRODUCT: tariff line
            product_key = f"PRODUCT:{addon_name}"
            ref_line = next((t for t in s["tariffLines"] if t["partyChargeTypeId"] == product_key), None)
            if ref_line:
                ref_amt = ref_line["amountDkk"]
                if has_obs:
                    calc_amt = sum(period_obs.values()) * addon_rate
                else:
                    calc_amt = total_kwh * addon_rate
                d = calc_amt - ref_amt
                if abs(d) > 0.50:
                    addon_diffs.append(f"{addon_name}: calc={calc_amt:.2f} ref={ref_amt:.2f} Δ={d:+.2f}")

        # 3d: Subscription verification
        sub_diffs = []
        for t in s["tariffLines"]:
            if t["isSubscription"] and not t["partyChargeTypeId"].startswith("PRODUCT:"):
                cid = t["partyChargeTypeId"]
                monthly_rate = sub_price_at(cid, ps_raw)
                # Calculate days in period
                ps_dt = parse_ts(ps_raw)
                pe_dt = parse_ts(pe_raw)
                days = (pe_dt - ps_dt).total_seconds() / 86400
                # Subscription amount = monthly_rate × (days / 30.4375 roughly)
                # Actually in Xellent it's just the flat monthly amount
                # WattsOn SettlementCalculator does: dailyPrice × days
                # But Xellent stores the monthly amount, not daily
                # Just compare against the stored amount for now
                ref_amt = t["amountDkk"]
                if monthly_rate > 0 and abs(ref_amt) > 0.01:
                    # Check if the stored amount is close to the monthly rate
                    # (could be prorated for partial months)
                    ratio = ref_amt / monthly_rate if monthly_rate != 0 else 0
                    if abs(ratio - 1.0) > 0.15 and abs(ratio - days / 30.0) > 0.15:
                        sub_diffs.append(f"{cid}: amt={ref_amt:.2f} monthly={monthly_rate:.2f} days={days:.0f}")

        # === Determine overall status ===
        issues = []
        if not consistency_ok:
            issues.append("consistency")
        if obs_mismatched > 2:  # Allow 2 for DST transitions
            issues.append(f"obs({obs_mismatched})")
        if margin_diff is not None and abs(margin_diff) > 1.0:
            issues.append(f"margin(Δ{margin_diff:+.1f})")
        if addon_diffs:
            issues.append(f"addon({len(addon_diffs)})")

        is_ok = len(issues) == 0

        r = dict(
            i=i, ps=ps, pe=pe, kwh=total_kwh, has_obs=has_obs,
            obs_n=len(period_obs), prod=prod_name, model=model,
            consistency_ok=consistency_ok, consistency_errors=consistency_errors,
            obs_matched=obs_matched, obs_mismatched=obs_mismatched,
            obs_missing=obs_missing, obs_errors=obs_errors,
            recalc_spot=recalc_spot, recalc_margin=recalc_margin,
            margin_ref=margin_ref, margin_diff=margin_diff,
            addon_diffs=addon_diffs, sub_diffs=sub_diffs,
            dh_total_ref=dh_total_ref, product_total_ref=product_total_ref,
            issues=issues, ok=is_ok,
            spot_missing=spot_missing_hours if recalc_spot is not None else None,
        )
        results.append(r)

        # === Print ===
        if not has_obs:
            icon = "⏭️"
        elif is_ok:
            icon = "✅"
        else:
            icon = "❌"

        margin_str = f"Margin={margin_ref:>7.2f}"
        if margin_diff is not None:
            margin_str += f"(Δ{margin_diff:>+6.2f})"

        spot_str = ""
        if recalc_spot is not None:
            spot_str = f" Spot={recalc_spot:>7.1f}"
            if r["spot_missing"]:
                spot_str += f"(!{r['spot_missing']}h)"

        obs_str = ""
        if has_obs:
            obs_str = f" obs={obs_matched}/{obs_matched + obs_mismatched + obs_missing}"
            if obs_mismatched:
                obs_str += f"(‽{obs_mismatched})"

        issue_str = f"  [{', '.join(issues)}]" if issues else ""

        print(f"{icon} [{i:2d}] {ps[:10]}→{pe[:10]}  {total_kwh:>6.1f}kWh  "
              f"DH={dh_total_ref:>8.2f}  {margin_str}  "
              f"({model[:5]:5s} {prod_name[:18]:18s})"
              f"{spot_str}{obs_str}{issue_str}")

        if args.verbose:
            if consistency_errors:
                for e in consistency_errors:
                    print(f"      ⚠️  {e}")
            if obs_errors:
                for e in obs_errors:
                    print(f"      ⚠️  obs {e}")
            if addon_diffs:
                for e in addon_diffs:
                    print(f"      ⚠️  {e}")
            if sub_diffs:
                for e in sub_diffs:
                    print(f"      ⚠️  sub {e}")

        # Deep mode: per-hour price analysis
        if args.deep and args.index is not None:
            print(f"\n  === Deep analysis: Settlement [{i}] ===")
            for t in s["tariffLines"]:
                cid = t["partyChargeTypeId"]
                sub = "SUB" if t["isSubscription"] else "TAR"
                print(f"\n  {cid} [{sub}]  amt={t['amountDkk']:.4f}  kwh={t['energyKwh']:.1f}  avg={t['avgUnitPrice']}")
                if t.get("hourlyDetail") and not t["isSubscription"]:
                    hd = t["hourlyDetail"]
                    rates = defaultdict(int)
                    for h in hd:
                        rates[h["rateDkkPerKwh"]] += 1
                    print(f"    Rates: {dict(sorted(rates.items()))}")
                    # Show first/last 3 hours
                    for h in hd[:3]:
                        obs_kwh = period_obs.get(norm(h["timestamp"]), "?")
                        sp = spot_lookup.get(norm(h["timestamp"]), "?")
                        print(f"    {h['timestamp'][:19]}  kwh={h['kwh']:.3f}  rate={h['rateDkkPerKwh']:.6f}  "
                              f"amt={h['amountDkk']:.6f}  obs={obs_kwh}  spot={sp}")
                    if len(hd) > 6:
                        print(f"    ... ({len(hd) - 6} more hours)")
                    for h in hd[-3:]:
                        obs_kwh = period_obs.get(norm(h["timestamp"]), "?")
                        sp = spot_lookup.get(norm(h["timestamp"]), "?")
                        print(f"    {h['timestamp'][:19]}  kwh={h['kwh']:.3f}  rate={h['rateDkkPerKwh']:.6f}  "
                              f"amt={h['amountDkk']:.6f}  obs={obs_kwh}  spot={sp}")

    # === Summary ===
    with_obs = [r for r in results if r["has_obs"]]
    without_obs = [r for r in results if not r["has_obs"]]
    ok_list = [r for r in with_obs if r["ok"]]

    print(f"\n{'═' * 60}")
    print(f"  Total settlements:  {len(results)}")
    print(f"  With observations:  {len(with_obs)}")
    print(f"  Without (skipped):  {len(without_obs)}")
    if with_obs:
        print(f"  ✅ Validated:       {len(ok_list)}/{len(with_obs)}")

        # Consistency check across all
        all_consistent = all(r["consistency_ok"] for r in results)
        print(f"  Internal consistency: {'✅ ALL' if all_consistent else '❌ SOME FAILED'}")

        # Margin analysis
        margin_diffs = [r["margin_diff"] for r in with_obs if r["margin_diff"] is not None]
        if margin_diffs:
            total_margin_diff = sum(abs(d) for d in margin_diffs)
            max_margin_diff = max(abs(d) for d in margin_diffs)
            print(f"  Margin Σ|Δ|:        {total_margin_diff:.2f} DKK (max={max_margin_diff:.2f})")

        # Spot analysis
        spot_totals = [r["recalc_spot"] for r in with_obs if r["recalc_spot"] is not None]
        if spot_totals:
            print(f"  Spot total (recalc): {sum(spot_totals):.2f} DKK across {len(spot_totals)} periods")
            missing = sum(r["spot_missing"] for r in with_obs if r["spot_missing"])
            if missing:
                print(f"  ⚠️  Missing spot hours: {missing}")

        # Observation analysis
        total_obs_match = sum(r["obs_matched"] for r in with_obs)
        total_obs_mismatch = sum(r["obs_mismatched"] for r in with_obs)
        total_obs_missing = sum(r["obs_missing"] for r in with_obs)
        print(f"  Obs matching:       {total_obs_match} ok, {total_obs_mismatch} mismatch, {total_obs_missing} missing")

        if with_obs and not ok_list:
            print("\n  ⚠️  All settlements with obs have issues — check with -v for details")

    # Issue breakdown
    issue_types = defaultdict(int)
    for r in results:
        for iss in r["issues"]:
            issue_types[iss.split("(")[0]] += 1
    if issue_types:
        print(f"\n  Issue breakdown:")
        for k, v in sorted(issue_types.items(), key=lambda x: -x[1]):
            print(f"    {k}: {v}")


if __name__ == "__main__":
    main()
