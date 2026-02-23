using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

/// <summary>
/// Bulk import endpoints for migrating data from an existing settlement system.
/// These bypass BRS process flows and insert directly — intended for one-time migration only.
/// All endpoints are prefixed with /api/migration/.
/// </summary>
public static class MigrationEndpoints
{
    public static WebApplication MapMigrationEndpoints(this WebApplication app)
    {
        // ==================== KUNDER (Customers with supplies + metering points) ====================

        /// <summary>
        /// Import customers with their metering points and supplies in one call.
        /// Creates customer → metering point → supply chain for each entry.
        /// Skips existing customers (matched by CPR/CVR) and existing metering points (matched by GSRN).
        /// </summary>
        app.MapPost("/api/migration/customers", async (MigrationCustomerBatchRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FindAsync(req.SupplierIdentityId);
            if (identity is null)
                return Results.BadRequest(new { error = "SupplierIdentity not found" });

            int customersCreated = 0, meteringPointsCreated = 0, suppliesCreated = 0, skipped = 0;

            foreach (var c in req.Customers)
            {
                // Check for existing customer by CPR or CVR
                Customer? existing = null;
                if (!string.IsNullOrEmpty(c.Cpr))
                {
                    var cpr = CprNumber.Create(c.Cpr);
                    existing = await db.Customers.FirstOrDefaultAsync(x => x.Cpr != null && x.Cpr.Value == cpr.Value);
                }
                if (existing is null && !string.IsNullOrEmpty(c.Cvr))
                {
                    var cvr = CvrNumber.Create(c.Cvr);
                    existing = await db.Customers.FirstOrDefaultAsync(x => x.Cvr != null && x.Cvr.Value == cvr.Value);
                }

                if (existing is not null)
                {
                    skipped++;
                    continue;
                }

                Customer customer;
                if (!string.IsNullOrEmpty(c.Cpr))
                    customer = Customer.CreatePerson(c.Name, CprNumber.Create(c.Cpr), req.SupplierIdentityId);
                else if (!string.IsNullOrEmpty(c.Cvr))
                    customer = Customer.CreateCompany(c.Name, CvrNumber.Create(c.Cvr), req.SupplierIdentityId);
                else
                    return Results.BadRequest(new { error = $"Customer '{c.Name}' must have either CPR or CVR" });

                if (c.Email is not null || c.Phone is not null)
                    customer.UpdateContactInfo(c.Email, c.Phone);
                db.Customers.Add(customer);

                foreach (var mp in c.MeteringPoints ?? [])
                {
                    var existingMp = await db.MeteringPoints.FirstOrDefaultAsync(x => x.Gsrn.Value == mp.Gsrn);
                    if (existingMp is not null) continue;

                    var gsrn = Gsrn.Create(mp.Gsrn);
                    var mpType = Enum.Parse<MeteringPointType>(mp.Type);
                    var art = Enum.Parse<MeteringPointCategory>(mp.Art);
                    var settlementMethod = Enum.Parse<SettlementMethod>(mp.SettlementMethod);
                    var resolution = !string.IsNullOrEmpty(mp.Resolution)
                        ? Enum.Parse<Resolution>(mp.Resolution) : Resolution.PT1H;

                    var meteringPoint = MeteringPoint.Create(gsrn, mpType, art, settlementMethod,
                        resolution, mp.GridArea ?? "DK1",
                        GlnNumber.Create(mp.GridOperatorGln ?? "5790000432752"));
                    db.MeteringPoints.Add(meteringPoint);
                    meteringPointsCreated++;

                    if (mp.SupplyStart.HasValue)
                    {
                        var period = mp.SupplyEnd.HasValue
                            ? Period.Create(mp.SupplyStart.Value, mp.SupplyEnd.Value)
                            : Period.From(mp.SupplyStart.Value);
                        var supply = Supply.Create(meteringPoint.Id, customer.Id, period);
                        db.Supplies.Add(supply);
                        suppliesCreated++;
                    }
                }

                customersCreated++;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                customersCreated,
                meteringPointsCreated,
                suppliesCreated,
                skipped,
                message = $"Migrering: {customersCreated} kunder, {meteringPointsCreated} målepunkter, {suppliesCreated} tilknytninger oprettet. {skipped} sprunget over."
            });
        }).WithName("MigrateCustomers");

        // ==================== PRODUKTER (Supplier products) ====================

        /// <summary>
        /// Import supplier products (the product catalog).
        /// Creates products for a supplier identity. Skips existing (matched by name).
        /// </summary>
        app.MapPost("/api/migration/supplier-products", async (MigrationSupplierProductBatchRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FindAsync(req.SupplierIdentityId);
            if (identity is null)
                return Results.BadRequest(new { error = "SupplierIdentity not found" });

            int created = 0, skipped = 0;
            var productMap = new Dictionary<string, Guid>(); // name → id

            foreach (var p in req.Products)
            {
                var existing = await db.SupplierProducts
                    .FirstOrDefaultAsync(x => x.SupplierIdentityId == req.SupplierIdentityId && x.Name == p.Name);
                if (existing is not null)
                {
                    productMap[p.Name] = existing.Id;
                    skipped++;
                    continue;
                }

                var pricingModel = Enum.TryParse<Domain.Enums.PricingModel>(p.PricingModel, true, out var pm)
                    ? pm : Domain.Enums.PricingModel.SpotAddon;
                var product = SupplierProduct.Create(req.SupplierIdentityId, p.Name, pricingModel, p.Description);
                if (!p.IsActive) product.Deactivate();
                db.SupplierProducts.Add(product);
                productMap[p.Name] = product.Id;
                created++;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                created,
                skipped,
                products = productMap,
                message = $"Migrering: {created} produkter oprettet, {skipped} sprunget over."
            });
        }).WithName("MigrateSupplierProducts");

        // ==================== PRODUKTPERIODER (Supply product periods) ====================

        /// <summary>
        /// Import product history for supplies — which product was active when.
        /// Matches supplies by GSRN + customer CPR/CVR.
        /// </summary>
        app.MapPost("/api/migration/supply-product-periods", async (MigrationSupplyProductPeriodBatchRequest req, WattsOnDbContext db) =>
        {
            int created = 0, skipped = 0;

            foreach (var pp in req.Periods)
            {
                // Find supply by GSRN
                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == pp.Gsrn);
                if (mp is null) { skipped++; continue; }

                var supply = await db.Supplies
                    .Where(s => s.MeteringPointId == mp.Id)
                    .Where(s => s.SupplyPeriod.Start <= pp.ProductStart)
                    .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > pp.ProductStart)
                    .FirstOrDefaultAsync();
                if (supply is null) { skipped++; continue; }

                // Find product by name + supplier identity
                var product = await db.SupplierProducts
                    .FirstOrDefaultAsync(p => p.Name == pp.ProductName && p.SupplierIdentityId == req.SupplierIdentityId);
                if (product is null) { skipped++; continue; }

                var period = pp.ProductEnd.HasValue
                    ? Period.Create(pp.ProductStart, pp.ProductEnd)
                    : Period.From(pp.ProductStart);

                // Check for existing period (dedup on re-push)
                var exists = await db.SupplyProductPeriods.AnyAsync(x =>
                    x.SupplyId == supply.Id && x.SupplierProductId == product.Id
                    && x.Period.Start == pp.ProductStart);
                if (exists) { skipped++; continue; }

                var spp = SupplyProductPeriod.Create(supply.Id, product.Id, period);
                db.SupplyProductPeriods.Add(spp);
                created++;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                created,
                skipped,
                message = $"Migrering: {created} produktperioder oprettet, {skipped} sprunget over."
            });
        }).WithName("MigrateSupplyProductPeriods");

        // ==================== DATAHUB-PRISER (Charges with price points) ====================

        /// <summary>
        /// Import DataHub charges (nettarif, systemtarif, elafgift, etc.) with their price points.
        /// These are the regulated prices that arrive via BRS-031/037 in production.
        /// For migration, import historical charges from the previous system.
        /// </summary>
        app.MapPost("/api/migration/prices", async (MigrationPriceBatchRequest req, WattsOnDbContext db) =>
        {
            int created = 0, updated = 0, pointsCreated = 0;

            foreach (var p in req.Prices)
            {
                var ownerGln = GlnNumber.Create(p.OwnerGln);
                var priceType = Enum.Parse<PriceType>(p.Type);
                var resolution = !string.IsNullOrEmpty(p.Resolution)
                    ? Enum.Parse<Resolution>(p.Resolution)
                    : (Resolution?)null;
                var category = !string.IsNullOrEmpty(p.Category)
                    ? Enum.Parse<PriceCategory>(p.Category)
                    : PriceCategory.Andet;

                var existing = await db.Prices
                    .Include(x => x.PricePoints)
                    .Where(x => x.ChargeId == p.ChargeId)
                    .FirstOrDefaultAsync(x => x.OwnerGln.Value == ownerGln.Value);

                Price price;
                if (existing is null)
                {
                    price = Price.Create(
                        p.ChargeId, ownerGln, priceType, p.Description,
                        Period.From(p.EffectiveDate), false, resolution,
                        p.IsTax, p.IsPassThrough, category: category);
                    db.Prices.Add(price);
                    created++;
                }
                else
                {
                    price = existing;
                    price.UpdatePriceInfo(p.Description, p.IsTax, p.IsPassThrough);
                    price.UpdateCategory(category);
                    price.UpdateValidity(Period.From(p.EffectiveDate));
                    updated++;
                }

                if (p.Points is { Count: > 0 })
                {
                    var start = p.Points.Min(pt => pt.Timestamp);
                    var end = p.Points.Max(pt => pt.Timestamp).AddHours(1);
                    var tuples = p.Points.Select(pt => (pt.Timestamp, pt.Price)).ToList();
                    price.ReplacePricePoints(start, end, tuples);
                    pointsCreated += p.Points.Count;
                }
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                created,
                updated,
                pointsCreated,
                message = $"Migrering: {created} nye priser, {updated} opdaterede, {pointsCreated} prispunkter."
            });
        }).WithName("MigratePrices");

        // ==================== SPOTPRISER ====================

        /// <summary>
        /// Bulk import historical spot prices.
        /// Delegates to SpotPriceService for upsert logic.
        /// </summary>
        app.MapPost("/api/migration/spot-prices", async (MigrationSpotPriceBatchRequest req, WattsOnDbContext db) =>
        {
            int totalInserted = 0, totalUpdated = 0;

            foreach (var batch in req.Batches)
            {
                if (batch.PriceArea != "DK1" && batch.PriceArea != "DK2")
                    return Results.BadRequest(new { error = $"PriceArea must be DK1 or DK2, got '{batch.PriceArea}'" });

                var timestamps = batch.Points.Select(p => p.Timestamp).ToList();
                if (timestamps.Count == 0) continue;

                var existing = await db.SpotPrices
                    .Where(sp => sp.PriceArea == batch.PriceArea &&
                                 sp.Timestamp >= timestamps.Min() && sp.Timestamp <= timestamps.Max())
                    .ToDictionaryAsync(sp => sp.Timestamp);

                var points = batch.Points.Select(p => (p.Timestamp, p.PriceDkkPerKwh)).ToList();
                var result = SpotPriceService.Upsert(batch.PriceArea, points, existing, e => db.SpotPrices.Add(e));
                totalInserted += result.Inserted;
                totalUpdated += result.Updated;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                inserted = totalInserted,
                updated = totalUpdated,
                total = totalInserted + totalUpdated,
                message = $"Migrering: {totalInserted} nye spotpriser, {totalUpdated} opdaterede."
            });
        }).WithName("MigrateSpotPrices");

        // ==================== LEVERANDØRMARGIN ====================

        /// <summary>
        /// Bulk import historical supplier margins for a product.
        /// Delegates to SupplierMarginService for upsert logic.
        /// </summary>
        app.MapPost("/api/migration/supplier-margins", async (MigrationSupplierMarginBatchRequest req, WattsOnDbContext db) =>
        {
            var product = await db.SupplierProducts.FindAsync(req.SupplierProductId);
            if (product is null)
                return Results.BadRequest(new { error = "SupplierProduct not found" });

            if (req.Rates == null || req.Rates.Count == 0)
                return Results.BadRequest(new { error = "Rates required" });

            var validFroms = req.Rates.Select(r => r.ValidFrom).ToList();
            var existing = await db.SupplierMargins
                .Where(m => m.SupplierProductId == req.SupplierProductId &&
                             m.ValidFrom >= validFroms.Min() && m.ValidFrom <= validFroms.Max())
                .ToDictionaryAsync(m => m.ValidFrom);

            var rates = req.Rates.Select(r => (r.ValidFrom, r.PriceDkkPerKwh)).ToList();
            var result = SupplierMarginService.Upsert(req.SupplierProductId, rates, existing, e => db.SupplierMargins.Add(e));

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                supplierProductId = req.SupplierProductId,
                inserted = result.Inserted,
                updated = result.Updated,
                total = result.Inserted + result.Updated,
                message = $"Migrering: {result.Inserted} nye marginer, {result.Updated} opdaterede."
            });
        }).WithName("MigrateSupplierMargins");

        // ==================== TIDSSERIER (Historical consumption) ====================

        /// <summary>
        /// Bulk import historical time series (consumption data) for metering points.
        /// Inserts raw observations — settlement engine can then calculate from these.
        /// </summary>
        app.MapPost("/api/migration/time-series", async (MigrationTimeSeriesBatchRequest req, WattsOnDbContext db) =>
        {
            int seriesCreated = 0, observationsCreated = 0, skipped = 0;

            foreach (var ts in req.TimeSeries)
            {
                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == ts.Gsrn);
                if (mp is null)
                {
                    skipped++;
                    continue;
                }

                var resolution = !string.IsNullOrEmpty(ts.Resolution)
                    ? Enum.Parse<Resolution>(ts.Resolution)
                    : Resolution.PT1H;

                var period = Period.Create(ts.PeriodStart, ts.PeriodEnd);
                var series = TimeSeries.Create(mp.Id, period, resolution, ts.Version ?? 1);
                db.TimeSeriesCollection.Add(series);
                seriesCreated++;

                foreach (var obs in ts.Observations)
                {
                    var quantity = EnergyQuantity.Create(obs.Kwh);
                    var quality = ParseQuality(obs.Quality);
                    series.AddObservation(obs.Timestamp, quantity, quality);
                    observationsCreated++;
                }
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                seriesCreated,
                observationsCreated,
                skipped,
                message = $"Migrering: {seriesCreated} tidsserier, {observationsCreated} observationer. {skipped} sprunget over (ukendt GSRN)."
            });
        }).WithName("MigrateTimeSeries");

        // ==================== PRISTILKNYTNINGER (Price links) ====================

        /// <summary>
        /// Bulk import price-to-metering-point links.
        /// Links DataHub charges to specific metering points (as received via BRS-037).
        /// </summary>
        app.MapPost("/api/migration/price-links", async (MigrationPriceLinkBatchRequest req, WattsOnDbContext db) =>
        {
            int created = 0, skipped = 0;

            foreach (var link in req.Links)
            {
                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == link.Gsrn);
                if (mp is null) { skipped++; continue; }

                var ownerGln = GlnNumber.Create(link.OwnerGln);

                // Map Xellent ChargeTypeCode to WattsOn PriceType for disambiguation
                // (same ChargeId+Owner can have both tariff and subscription entries)
                PriceType? expectedType = link.ChargeTypeCode switch
                {
                    1 => PriceType.Abonnement,
                    2 => PriceType.Gebyr,
                    3 => PriceType.Tarif,
                    _ => null
                };

                var priceQuery = db.Prices
                    .Where(p => p.ChargeId == link.ChargeId)
                    .Where(p => p.OwnerGln.Value == ownerGln.Value);

                if (expectedType.HasValue)
                    priceQuery = priceQuery.Where(p => p.Type == expectedType.Value);

                var price = await priceQuery.FirstOrDefaultAsync();
                if (price is null) { skipped++; continue; }

                var exists = await db.PriceLinks.AnyAsync(pl =>
                    pl.MeteringPointId == mp.Id && pl.PriceId == price.Id);
                if (exists) { skipped++; continue; }

                var linkPeriod = link.EndDate.HasValue
                    ? Period.Create(link.EffectiveDate, link.EndDate.Value)
                    : Period.From(link.EffectiveDate);
                var priceLink = PriceLink.Create(mp.Id, price.Id, linkPeriod);
                db.PriceLinks.Add(priceLink);
                created++;
            }

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                created,
                skipped,
                message = $"Migrering: {created} pristilknytninger oprettet, {skipped} sprunget over."
            });
        }).WithName("MigratePriceLinks");

        // ==================== AFREGNINGER (Settlements) ====================

        /// <summary>
        /// Import pre-computed settlements (from FlexBillingHistory).
        /// Creates settlements with Status = Invoiced for correction detection baseline.
        /// </summary>
        app.MapPost("/api/migration/settlements", async (MigrationSettlementBatchRequest req, WattsOnDbContext db) =>
        {
            int created = 0, skippedNoMp = 0, skippedNoSupply = 0, skippedExists = 0;

            foreach (var s in req.Settlements)
            {
                // Find metering point by GSRN
                var mp = await db.MeteringPoints
                    .FirstOrDefaultAsync(m => m.Gsrn.Value == s.Gsrn);
                if (mp is null) { skippedNoMp++; continue; }

                // Find supply for this metering point.
                // For migration: try period match first, fall back to ANY supply on the MP.
                // Migrated settlements are historical baselines — the supply link is for FK integrity,
                // not for validating whether the supply was active during that period.
                var supply = await db.Supplies
                    .Where(l => l.MeteringPointId == mp.Id)
                    .Where(l => l.SupplyPeriod.Start <= s.PeriodStart)
                    .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > s.PeriodStart)
                    .FirstOrDefaultAsync();
                supply ??= await db.Supplies
                    .Where(l => l.MeteringPointId == mp.Id)
                    .FirstOrDefaultAsync();
                if (supply is null) { skippedNoSupply++; continue; }

                // Check if settlement already exists for this period
                var existing = await db.Settlements
                    .AnyAsync(a => a.MeteringPointId == mp.Id
                        && a.SettlementPeriod.Start == s.PeriodStart
                        && a.SettlementPeriod.End == s.PeriodEnd);
                if (existing) { skippedExists++; continue; }

                // Find or create a time series for this period (settlements need a TS reference)
                var timeSeries = await db.TimeSeriesCollection
                    .Where(ts => ts.MeteringPointId == mp.Id)
                    .Where(ts => ts.Period.Start == s.PeriodStart)
                    .Where(ts => ts.Period.End == s.PeriodEnd)
                    .FirstOrDefaultAsync();

                if (timeSeries is null)
                {
                    // Create a placeholder time series — the actual observations may come from time series migration
                    timeSeries = TimeSeries.Create(mp.Id,
                        Period.Create(s.PeriodStart, s.PeriodEnd),
                        Resolution.PT1H, 1);
                    db.TimeSeriesCollection.Add(timeSeries);
                    await db.SaveChangesAsync();
                }

                // Serialize Xellent hourly data if provided
                string? hourlyJson = null;
                if (s.HourlyLines is { Count: > 0 })
                {
                    hourlyJson = System.Text.Json.JsonSerializer.Serialize(
                        s.HourlyLines.Select(h => new { t = h.Timestamp, k = h.Kwh, s = h.SpotPrice, c = h.CalcPrice }),
                        new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                }

                // Create migrated settlement — clearly marked as imported, not calculated by WattsOn
                var settlement = Settlement.CreateMigrated(
                    mp.Id, supply.Id,
                    Period.Create(s.PeriodStart, s.PeriodEnd),
                    timeSeries.Id, timeSeries.Version,
                    EnergyQuantity.Create(s.TotalEnergyKwh),
                    s.ExternalInvoiceReference ?? s.BillingLogNum,
                    hourlyJson);

                // Add spot line
                if (s.SpotAmountDkk != 0)
                {
                    var spotLine = SettlementLine.CreateSpot(
                        settlement.Id, "Spotpris (migreret)",
                        EnergyQuantity.Create(s.TotalEnergyKwh),
                        s.TotalEnergyKwh != 0 ? s.SpotAmountDkk / s.TotalEnergyKwh : 0);
                    settlement.AddLine(spotLine);
                }

                // Add aggregate margin line — only if no per-product PRODUCT: lines
                // (new extractions break margin into PRODUCT: lines; old cache files use aggregate)
                var hasProductLines = s.TariffLines?.Any(t => t.ChargeId.StartsWith("PRODUCT:")) == true;
                if (s.MarginAmountDkk != 0 && !hasProductLines)
                {
                    var marginLine = SettlementLine.CreateMargin(
                        settlement.Id, "Leverandørmargin (migreret)",
                        EnergyQuantity.Create(s.TotalEnergyKwh),
                        s.TotalEnergyKwh != 0 ? s.MarginAmountDkk / s.TotalEnergyKwh : 0);
                    settlement.AddLine(marginLine);
                }

                // Add tariff/charge lines — routed to correct Source by type:
                //   PRODUCT:* → SupplierMargin (product margin from ExuRateTable)
                //   Subscriptions/fees → DataHubCharge flat amount (abonnement/gebyr)
                //   Tariffs → DataHubCharge per-kWh (with Price FK when available)
                if (s.TariffLines != null)
                {
                    foreach (var tariff in s.TariffLines)
                    {
                        if (tariff.ChargeId.StartsWith("PRODUCT:"))
                        {
                            // Product margins → SupplierMargin source
                            // These are per-kWh rates from ExuRateTable (supplier's own pricing)
                            var energy = EnergyQuantity.Create(tariff.EnergyKwh);
                            var unitPrice = tariff.EnergyKwh != 0
                                ? (tariff.AmountDkk ?? tariff.EnergyKwh * tariff.AvgUnitPrice) / tariff.EnergyKwh
                                : tariff.AvgUnitPrice;
                            var productLine = SettlementLine.CreateMargin(
                                settlement.Id,
                                $"{tariff.Description} (migreret)",
                                energy, unitPrice);
                            settlement.AddLine(productLine);
                        }
                        else if (tariff.IsSubscription)
                        {
                            // Subscriptions/fees → DataHubCharge, flat monthly amount
                            // Not per-kWh — the amount IS the fixed charge
                            var amount = tariff.AmountDkk ?? 0;
                            var subLine = SettlementLine.CreateMigratedSubscription(
                                settlement.Id,
                                $"{tariff.Description} [{tariff.ChargeId}] (migreret, abonnement)",
                                amount);
                            settlement.AddLine(subLine);
                        }
                        else
                        {
                            // Regular tariffs → DataHubCharge, per-kWh
                            // Link to Price entity if we have a matching DataHub charge
                            var price = await db.Prices
                                .FirstOrDefaultAsync(p => p.ChargeId == tariff.ChargeId);

                            var energy = EnergyQuantity.Create(tariff.EnergyKwh);
                            var unitPrice = tariff.AmountDkk.HasValue && tariff.EnergyKwh != 0
                                ? tariff.AmountDkk.Value / tariff.EnergyKwh
                                : tariff.AvgUnitPrice;

                            var tariffLine = price is not null
                                ? SettlementLine.Create(
                                    settlement.Id, price.Id,
                                    $"{tariff.Description} (migreret)",
                                    energy, unitPrice)
                                : SettlementLine.CreateMigrated(
                                    settlement.Id,
                                    $"{tariff.Description} [{tariff.ChargeId}] (migreret)",
                                    energy, unitPrice);
                            settlement.AddLine(tariffLine);
                        }
                    }
                }

                db.Settlements.Add(settlement);
                created++;
            }

            await db.SaveChangesAsync();

            var skipped = skippedNoMp + skippedNoSupply + skippedExists;
            return Results.Ok(new
            {
                created,
                skipped,
                skippedNoMp,
                skippedNoSupply,
                skippedExists,
                message = $"Migrering: {created} afregninger oprettet. Sprunget over: {skippedNoMp} ukendt MP, {skippedNoSupply} ingen tilknytning, {skippedExists} eksisterer allerede."
            });
        }).WithName("MigrateSettlements");

        return app;
    }

    private static QuantityQuality ParseQuality(string? quality) => quality switch
    {
        "A01" or "Measured" or null => QuantityQuality.Measured,
        "A02" or "Estimated" => QuantityQuality.Estimated,
        "A03" or "Calculated" => QuantityQuality.Calculated,
        "A04" or "NotAvailable" => QuantityQuality.NotAvailable,
        "A05" or "Revised" => QuantityQuality.Revised,
        "E01" or "Adjusted" => QuantityQuality.Adjusted,
        _ => QuantityQuality.Measured
    };
}

// ==================== Migration DTOs ====================

record MigrationCustomerBatchRequest(
    Guid SupplierIdentityId,
    List<MigrationCustomerDto> Customers);

record MigrationCustomerDto(
    string Name,
    string? Cpr,
    string? Cvr,
    string? Email,
    string? Phone,
    List<MigrationMeteringPointDto>? MeteringPoints);

record MigrationMeteringPointDto(
    string Gsrn,
    string Type,       // Consumption, Production, Exchange
    string Art,         // Physical, Virtual, Calculated
    string SettlementMethod,  // Flex, NonProfiled, Profiled
    string? Resolution,       // PT1H, PT15M — defaults to PT1H
    string? GridArea,
    string? GridOperatorGln,
    DateTimeOffset? SupplyStart,
    DateTimeOffset? SupplyEnd = null);

record MigrationSupplierProductBatchRequest(
    Guid SupplierIdentityId,
    List<MigrationSupplierProductDto> Products);

record MigrationSupplierProductDto(
    string Name,
    string? Description,
    string PricingModel = "SpotAddon",
    bool IsActive = true);

record MigrationSupplyProductPeriodBatchRequest(
    Guid SupplierIdentityId,
    List<MigrationSupplyProductPeriodDto> Periods);

record MigrationSupplyProductPeriodDto(
    string Gsrn,
    string ProductName,
    DateTimeOffset ProductStart,
    DateTimeOffset? ProductEnd);

record MigrationPriceBatchRequest(
    List<MigrationPriceDto> Prices);

record MigrationPriceDto(
    string ChargeId,
    string OwnerGln,
    string Type,           // Tarif, Gebyr, Abonnement
    string Description,
    DateTimeOffset EffectiveDate,
    string? Resolution,    // PT1H, PT15M, P1D, P1M
    bool IsTax,
    bool IsPassThrough,
    string? Category,      // Nettarif, Systemtarif, Transmissionstarif, Elafgift, etc.
    List<PricePointDto>? Points);

record MigrationSpotPriceBatchRequest(
    List<MigrationSpotPriceBatchDto> Batches);

record MigrationSpotPriceBatchDto(
    string PriceArea,      // DK1 or DK2
    List<SpotPricePointDto> Points);

record MigrationSupplierMarginBatchRequest(
    Guid SupplierProductId,
    List<MarginRateDto> Rates);

record MigrationTimeSeriesBatchRequest(
    List<MigrationTimeSeriesDto> TimeSeries);

record MigrationTimeSeriesDto(
    string Gsrn,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string? Resolution,    // PT1H, PT15M
    int? Version,          // defaults to 1
    List<MigrationObservationDto> Observations);

record MigrationObservationDto(
    DateTimeOffset Timestamp,
    decimal Kwh,
    string? Quality);      // A01 (measured), A02 (estimated), etc.

record MigrationPriceLinkBatchRequest(
    List<MigrationPriceLinkDto> Links);

record MigrationPriceLinkDto(
    string Gsrn,
    string ChargeId,
    string OwnerGln,
    DateTimeOffset EffectiveDate,
    DateTimeOffset? EndDate = null,
    int ChargeTypeCode = 0);

record MigrationSettlementBatchRequest(
    List<MigrationSettlementDto> Settlements);

record MigrationSettlementDto(
    string Gsrn,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string BillingLogNum,
    string? ExternalInvoiceReference,
    decimal TotalEnergyKwh,
    decimal SpotAmountDkk,
    decimal MarginAmountDkk,
    List<MigrationSettlementTariffLineDto>? TariffLines,
    List<MigrationHourlyLineDto>? HourlyLines = null);

record MigrationSettlementTariffLineDto(
    string ChargeId,
    string Description,
    decimal EnergyKwh,
    decimal AvgUnitPrice,
    decimal? AmountDkk = null,
    bool IsSubscription = false);

record MigrationHourlyLineDto(
    DateTimeOffset Timestamp,
    decimal Kwh,
    decimal SpotPrice,
    decimal CalcPrice);
