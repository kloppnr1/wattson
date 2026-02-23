using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SettlementEndpoints
{
    public static WebApplication MapSettlementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settlements", async (WattsOnDbContext db) =>
        {
            var settlements = await db.Settlements
                .AsNoTracking()
                .OrderByDescending(a => a.SettlementPeriod.Start)
                .Take(100)
                .Select(a => new
                {
                    a.Id,
                    a.MeteringPointId,
                    a.SupplyId,
                    PeriodStart = a.SettlementPeriod.Start,
                    PeriodEnd = a.SettlementPeriod.End,
                    TotalEnergyKwh = a.TotalEnergy.Value,
                    TotalAmount = a.TotalAmount.Amount,
                    Currency = a.TotalAmount.Currency,
                    Status = a.Status.ToString(),
                    a.IsCorrection,
                    a.PreviousSettlementId,
                    a.ExternalInvoiceReference,
                    a.InvoicedAt,
                    a.CalculatedAt
                })
                .ToListAsync();
            return Results.Ok(settlements);
        }).WithName("GetSettlements");

        /// <summary>
        /// Uninvoiced settlements — for external invoicing system to pick up.
        /// Returns settlements with status = Beregnet that are NOT corrections.
        /// </summary>
        app.MapGet("/api/settlements/uninvoiced", async (WattsOnDbContext db) =>
        {
            var uninvoiced = await db.Settlements
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                .Where(a => a.Status == SettlementStatus.Calculated && !a.IsCorrection)
                .AsNoTracking()
                .OrderBy(a => a.CalculatedAt)
                .Select(a => new
                {
                    a.Id,
                    Gsrn = a.MeteringPoint.Gsrn.Value,
                    CustomerId = a.Supply.CustomerId,
                    CustomerNavn = a.Supply.Customer.Name,
                    PeriodStart = a.SettlementPeriod.Start,
                    PeriodEnd = a.SettlementPeriod.End,
                    TotalEnergyKwh = a.TotalEnergy.Value,
                    TotalAmount = a.TotalAmount.Amount,
                    Currency = a.TotalAmount.Currency,
                    a.CalculatedAt
                })
                .ToListAsync();
            return Results.Ok(uninvoiced);
        }).WithName("GetUninvoicedSettlements");

        /// <summary>
        /// Adjustment settlements — corrections of already-invoiced settlements.
        /// External invoicing system picks these up to issue credit/debit notes.
        /// </summary>
        app.MapGet("/api/settlements/adjustments", async (WattsOnDbContext db) =>
        {
            var adjustments = await db.Settlements
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                .Where(a => a.Status == SettlementStatus.Calculated && a.IsCorrection)
                .AsNoTracking()
                .OrderBy(a => a.CalculatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.PreviousSettlementId,
                    Gsrn = a.MeteringPoint.Gsrn.Value,
                    CustomerId = a.Supply.CustomerId,
                    CustomerNavn = a.Supply.Customer.Name,
                    PeriodStart = a.SettlementPeriod.Start,
                    PeriodEnd = a.SettlementPeriod.End,
                    DeltaEnergyKwh = a.TotalEnergy.Value,
                    DeltaAmount = a.TotalAmount.Amount,
                    Currency = a.TotalAmount.Currency,
                    a.CalculatedAt
                })
                .ToListAsync();
            return Results.Ok(adjustments);
        }).WithName("GetAdjustmentSettlements");

        /// <summary>
        /// Get the latest settlement for a metering point — used to watch the settlement engine work.
        /// Returns the full calculation breakdown (lines, prices, quantities, amounts).
        /// </summary>
        app.MapGet("/api/settlements/by-metering-point/{meteringPointId:guid}", async (Guid meteringPointId, WattsOnDbContext db) =>
        {
            var settlement = await db.Settlements
                .Include(a => a.Lines)
                .Include(a => a.MeteringPoint)
                .AsNoTracking()
                .Where(a => a.MeteringPointId == meteringPointId)
                .OrderByDescending(a => a.CalculatedAt)
                .FirstOrDefaultAsync();

            if (settlement is null) return Results.Ok(new { found = false });

            return Results.Ok(new
            {
                found = true,
                id = settlement.Id,
                meteringPointId = settlement.MeteringPointId,
                gsrn = settlement.MeteringPoint.Gsrn.Value,
                periodStart = settlement.SettlementPeriod.Start,
                periodEnd = settlement.SettlementPeriod.End,
                totalEnergyKwh = settlement.TotalEnergy.Value,
                totalAmount = settlement.TotalAmount.Amount,
                currency = settlement.TotalAmount.Currency,
                status = settlement.Status.ToString(),
                isCorrection = settlement.IsCorrection,
                timeSeriesVersion = settlement.TimeSeriesVersion,
                calculatedAt = settlement.CalculatedAt,
                lines = settlement.Lines.Select(l => new
                {
                    l.Id,
                    l.PriceId,
                    l.Description,
                    quantityKwh = l.Quantity.Value,
                    unitPrice = l.UnitPrice,
                    amount = l.Amount.Amount,
                    currency = l.Amount.Currency,
                }).ToList()
            });
        }).WithName("GetSettlementByMeteringPoint");

        /// <summary>
        /// External invoicing system confirms a settlement has been invoiced.
        /// </summary>
        app.MapPost("/api/settlements/{id:guid}/mark-invoiced", async (Guid id, MarkInvoicedRequest req, WattsOnDbContext db) =>
        {
            var settlement = await db.Settlements.FindAsync(id);
            if (settlement is null) return Results.NotFound();

            try
            {
                settlement.MarkInvoiced(req.ExternalInvoiceReference);
                await db.SaveChangesAsync();
                return Results.Ok(new
                {
                    settlement.Id,
                    Status = settlement.Status.ToString(),
                    settlement.ExternalInvoiceReference,
                    settlement.InvoicedAt
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).WithName("MarkSettlementInvoiced");

        // ==================== RECALCULATE (non-persistent) ====================

        /// <summary>
        /// Recalculate a settlement using WattsOn's engine — without persisting.
        /// Returns the recalculated result alongside the original for comparison.
        /// Useful for validating migrated settlements against WattsOn's own calculation.
        /// </summary>
        app.MapPost("/api/settlements/{id:guid}/recalculate", async (Guid id, WattsOnDbContext db) =>
        {
            // Load the original settlement with lines
            var original = await db.Settlements
                .Include(s => s.Lines)
                .Include(s => s.MeteringPoint)
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == id);

            if (original is null) return Results.NotFound();

            // Find the time series with actual observations for this metering point & period.
            // Migrated settlements reference stub time series (period-only, 0 obs).
            // The real observations live in a main time series covering the full supply period.
            var periodEnd = original.SettlementPeriod.End ?? original.SettlementPeriod.Start.AddMonths(1);

            var timeSeries = await db.TimeSeriesCollection
                .Include(ts => ts.Observations.Where(o =>
                    o.Timestamp >= original.SettlementPeriod.Start && o.Timestamp < periodEnd))
                .Where(ts => ts.MeteringPointId == original.MeteringPointId)
                .Where(ts => ts.Observations.Any(o =>
                    o.Timestamp >= original.SettlementPeriod.Start && o.Timestamp < periodEnd))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (timeSeries is null)
                return Results.Ok(new
                {
                    settlementId = original.Id,
                    status = original.Status.ToString(),
                    period = new { start = original.SettlementPeriod.Start, end = original.SettlementPeriod.End },
                    recalcError = "No observations found for settlement period",
                    original = new
                    {
                        totalEnergyKwh = original.TotalEnergy.Value,
                        totalAmount = original.TotalAmount.Amount,
                        lines = original.Lines.OrderBy(l => l.Description).Select(l => new
                        {
                            source = l.Source.ToString(),
                            description = l.Description,
                            quantityKwh = l.Quantity.Value,
                            unitPrice = l.UnitPrice,
                            amount = l.Amount.Amount,
                        }),
                    },
                });

            // Load the supply that was active at settlement period start
            var supply = await db.Supplies
                .Where(s => s.MeteringPointId == original.MeteringPointId)
                .Where(s => s.SupplyPeriod.Start <= original.SettlementPeriod.Start)
                .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > original.SettlementPeriod.Start)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            if (supply is null)
                return Results.BadRequest(new { error = "No active supply for settlement period" });

            // Load DataHub price links for this metering point at the settlement period
            var priceLinks = await db.PriceLinks
                .Include(pl => pl.Price)
                    .ThenInclude(p => p.PricePoints)
                .Where(pl => pl.MeteringPointId == original.MeteringPointId)
                .Where(pl => pl.LinkPeriod.Start <= original.SettlementPeriod.Start)
                .Where(pl => pl.LinkPeriod.End == null || pl.LinkPeriod.End > original.SettlementPeriod.Start)
                .AsNoTracking()
                .ToListAsync();

            // For migrated settlements, use a price point cutoff matching Xellent's rate resolution:
            // "latest rate with StartDate < periodStart". This prevents templates that start
            // mid-period from overriding the template that was in effect when Xellent billed.
            var pointsCutoff = original.Status == SettlementStatus.Migrated
                ? original.SettlementPeriod.Start
                : (DateTimeOffset?)null;

            var datahubPrices = priceLinks
                .Select(pl => new PriceWithPoints(pl.Price, pointsCutoff))
                .ToList();

            // Load spot prices for the period
            var spotPrices = await db.SpotPrices
                .Where(sp => sp.PriceArea == original.MeteringPoint.GridArea)
                .Where(sp => sp.Timestamp >= original.SettlementPeriod.Start && sp.Timestamp < periodEnd)
                .OrderBy(sp => sp.Timestamp)
                .AsNoTracking()
                .ToListAsync();

            // Load supplier margin via supply → product period → margin
            var activeProductPeriod = await db.SupplyProductPeriods
                .Where(pp => pp.SupplyId == supply.Id)
                .Where(pp => pp.Period.Start <= original.SettlementPeriod.Start)
                .Where(pp => pp.Period.End == null || pp.Period.End > original.SettlementPeriod.Start)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            // Load ALL active product periods for the supply at settlement time
            // (base product + addon products like "Grøn strøm").
            // Sum their margins for the combined supplier rate.
            var allProductPeriods = await db.SupplyProductPeriods
                .Where(pp => pp.SupplyId == supply.Id)
                .Where(pp => pp.Period.Start <= original.SettlementPeriod.Start)
                .Where(pp => pp.Period.End == null || pp.Period.End > original.SettlementPeriod.Start)
                .AsNoTracking()
                .ToListAsync();

            var pricingModel = PricingModel.SpotAddon;
            var namedMargins = new List<(string Name, SupplierMargin Margin)>();

            foreach (var pp in allProductPeriods)
            {
                var product = await db.SupplierProducts
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == pp.SupplierProductId);

                if (product is not null && activeProductPeriod is not null && pp.Id == activeProductPeriod.Id)
                    pricingModel = product.PricingModel;

                var margin = await db.SupplierMargins
                    .Where(m => m.SupplierProductId == pp.SupplierProductId)
                    .Where(m => m.ValidFrom <= original.SettlementPeriod.Start)
                    .OrderByDescending(m => m.ValidFrom)
                    .AsNoTracking()
                    .FirstOrDefaultAsync();

                if (margin is not null)
                {
                    var productName = product?.Name ?? "Leverandørmargin";
                    namedMargins.Add((productName, margin));
                }
            }

            var periodObs = timeSeries.Observations;

            // Pre-aggregate observations to hourly buckets.
            // Migration data may have duplicate timestamps or sub-hourly resolution for some days.
            var hourlyObs = periodObs
                .GroupBy(o => o.Timestamp.ToOffset(TimeSpan.Zero)
                    .AddTicks(-(o.Timestamp.ToOffset(TimeSpan.Zero).Ticks % TimeSpan.TicksPerHour)))
                .Select(g => new { Timestamp = g.Key, Kwh = g.Sum(o => o.Quantity.Value) })
                .OrderBy(o => o.Timestamp)
                .ToList();

            // Create a scoped time series for the settlement period.
            // The main time series may span months/years — we need one scoped to this settlement's period.
            var scopedTs = TimeSeries.Create(
                original.MeteringPointId,
                original.SettlementPeriod,
                timeSeries.Resolution,
                timeSeries.Version);

            // Use hourly-aggregated observations to avoid double-counting from duplicate timestamps
            scopedTs.AddObservations(hourlyObs.Select(o =>
                (o.Timestamp, EnergyQuantity.Create(o.Kwh), QuantityQuality.Measured)));

            // Recalculate using WattsOn's engine
            Settlement? recalculated = null;
            string? recalcError = null;

            try
            {
                recalculated = SettlementCalculator.Calculate(
                    scopedTs, supply, datahubPrices, spotPrices, namedMargins, pricingModel);
            }
            catch (Exception ex)
            {
                recalcError = ex.Message;
            }

            // .NET 6+ supports IANA on Windows, but fall back to Windows ID just in case
            TimeZoneInfo dkTz;
            try { dkTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"); }
            catch (TimeZoneNotFoundException) { dkTz = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time"); }

            // ---- Per-line detail builder (tariff tiers + daily/hourly, spot stats, subscription, margin) ----
            object? BuildLineDetails(SettlementLine line)
            {
                if (line.Source == SettlementLineSource.DataHubCharge && line.PriceId.HasValue)
                {
                    var pwp = datahubPrices.FirstOrDefault(p => p.Price.Id == line.PriceId.Value);
                    if (pwp is null) return null;

                    if (pwp.Price.Type == PriceType.Tarif)
                    {
                        if (hourlyObs.Count == 0)
                            return new { type = "tarif", totalHours = 0, hoursWithPrice = 0,
                                tiers = Array.Empty<object>(), daily = Array.Empty<object>() };

                        var obsData = hourlyObs.Select(obs =>
                        {
                            var rate = pwp.GetPriceAt(obs.Timestamp) ?? 0m;
                            var kwh = obs.Kwh;
                            var lt = TimeZoneInfo.ConvertTime(obs.Timestamp, dkTz);
                            return new { obs.Timestamp, Kwh = kwh, Rate = rate, Amount = kwh * rate,
                                LocalDate = lt.Date, LocalHour = lt.Hour };
                        }).ToList();

                        var tiers = obsData
                            .GroupBy(o => Math.Round(o.Rate, 6)).OrderBy(g => g.Key)
                            .Select(g => new { rate = g.Key, hours = g.Count(),
                                kwh = Math.Round(g.Sum(o => o.Kwh), 4),
                                amount = Math.Round(g.Sum(o => o.Kwh * o.Rate), 4) })
                            .ToList();

                        var daily = obsData
                            .GroupBy(o => o.LocalDate).OrderBy(g => g.Key)
                            .Select(g => new {
                                date = g.Key.ToString("yyyy-MM-dd"),
                                kwh = Math.Round(g.Sum(o => o.Kwh), 4),
                                amount = Math.Round(g.Sum(o => o.Amount), 4),
                                hours = g.OrderBy(o => o.Timestamp).Select(o => new {
                                    hour = o.LocalHour, kwh = Math.Round(o.Kwh, 4),
                                    rate = Math.Round(o.Rate, 6), amount = Math.Round(o.Amount, 4),
                                }).ToList(),
                            }).ToList();

                        return new { type = "tarif", totalHours = hourlyObs.Count,
                            hoursWithPrice = obsData.Count(o => o.Rate > 0), tiers, daily };
                    }

                    if (pwp.Price.Type == PriceType.Abonnement)
                    {
                        var days = (decimal)(periodEnd - original.SettlementPeriod.Start).TotalDays;
                        var dailyRate = pwp.GetPriceAt(original.SettlementPeriod.Start) ?? 0m;
                        return new { type = "abonnement", days, dailyRate, daily = (object?)null };
                    }
                }

                if (line.Source == SettlementLineSource.SpotPrice)
                {
                    if (hourlyObs.Count == 0)
                        return new { type = "spot", totalHours = 0, hoursWithPrice = 0, hoursMissing = 0,
                            avgRate = 0m, minRate = 0m, maxRate = 0m, daily = Array.Empty<object>() };

                    var spotLookup = spotPrices.ToDictionary(s => s.Timestamp);
                    var obsData = hourlyObs.Select(obs =>
                    {
                        spotLookup.TryGetValue(obs.Timestamp, out var sp);
                        var rate = sp?.PriceDkkPerKwh ?? 0m;
                        var kwh = obs.Kwh;
                        var lt = TimeZoneInfo.ConvertTime(obs.Timestamp, dkTz);
                        return new { obs.Timestamp, Kwh = kwh, Rate = rate, HasPrice = sp != null,
                            Amount = kwh * rate, LocalDate = lt.Date, LocalHour = lt.Hour };
                    }).ToList();

                    var withPrice = obsData.Where(h => h.HasPrice).ToList();
                    var totalKwh = withPrice.Sum(h => h.Kwh);

                    var daily = obsData
                        .GroupBy(o => o.LocalDate).OrderBy(g => g.Key)
                        .Select(g => new {
                            date = g.Key.ToString("yyyy-MM-dd"),
                            kwh = Math.Round(g.Sum(o => o.Kwh), 4),
                            amount = Math.Round(g.Sum(o => o.Amount), 4),
                            hours = g.OrderBy(o => o.Timestamp).Select(o => new {
                                hour = o.LocalHour, kwh = Math.Round(o.Kwh, 4),
                                rate = Math.Round(o.Rate, 6), amount = Math.Round(o.Amount, 4),
                            }).ToList(),
                        }).ToList();

                    return new {
                        type = "spot", totalHours = obsData.Count,
                        hoursWithPrice = withPrice.Count, hoursMissing = obsData.Count - withPrice.Count,
                        avgRate = totalKwh > 0 ? Math.Round(withPrice.Sum(h => h.Kwh * h.Rate) / totalKwh, 6) : 0m,
                        minRate = withPrice.Count > 0 ? Math.Round(withPrice.Min(h => h.Rate), 6) : 0m,
                        maxRate = withPrice.Count > 0 ? Math.Round(withPrice.Max(h => h.Rate), 6) : 0m,
                        daily,
                    };
                }

                if (line.Source == SettlementLineSource.SupplierMargin)
                {
                    var matchedMargin = namedMargins.FirstOrDefault(m => m.Name == line.Description);
                    return new { type = "margin",
                        ratePerKwh = matchedMargin.Margin?.PriceDkkPerKwh ?? line.UnitPrice,
                        daily = (object?)null };
                }

                return null;
            }

            // Build comparison response
            return Results.Ok(new
            {
                settlementId = original.Id,
                status = original.Status.ToString(),
                period = new { start = original.SettlementPeriod.Start, end = original.SettlementPeriod.End },
                pricingModel = pricingModel.ToString(),
                observationsInPeriod = hourlyObs.Count,
                spotPricesInPeriod = spotPrices.Count,
                datahubPriceLinks = datahubPrices.Count,
                marginRate = namedMargins.Sum(m => m.Margin.PriceDkkPerKwh),
                margins = namedMargins.Select(m => new { name = m.Name, rate = m.Margin.PriceDkkPerKwh }),

                // Xellent hourly data for migrated settlements (for kWh comparison)
                migratedHourly = !string.IsNullOrEmpty(original.MigratedHourlyJson)
                    ? System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(original.MigratedHourlyJson)
                    : (System.Text.Json.JsonElement?)null,

                original = new
                {
                    totalEnergyKwh = original.TotalEnergy.Value,
                    totalAmount = original.TotalAmount.Amount,
                    lines = original.Lines.OrderBy(l => l.Description).Select(l => new
                    {
                        source = l.Source.ToString(),
                        description = l.Description,
                        quantityKwh = l.Quantity.Value,
                        unitPrice = l.UnitPrice,
                        amount = l.Amount.Amount,
                        details = BuildLineDetails(l),
                    }),
                },

                recalculated = recalculated is not null ? new
                {
                    totalEnergyKwh = recalculated.TotalEnergy.Value,
                    totalAmount = recalculated.TotalAmount.Amount,
                    lines = recalculated.Lines.OrderBy(l => l.Description).Select(l => new
                    {
                        source = l.Source.ToString(),
                        description = l.Description,
                        quantityKwh = l.Quantity.Value,
                        unitPrice = l.UnitPrice,
                        amount = l.Amount.Amount,
                        details = BuildLineDetails(l),
                    }),
                } : null,

                recalcError,

                comparison = recalculated is not null ? new
                {
                    totalAmountDiff = recalculated.TotalAmount.Amount - original.TotalAmount.Amount,
                    totalEnergyDiff = recalculated.TotalEnergy.Value - original.TotalEnergy.Value,
                    lineDiffs = recalculated.Lines.OrderBy(l => l.Description).Select(rl =>
                    {
                        // Match by normalized description — migrated lines have "(migreret)" suffix
                        var normalDesc = rl.Description;
                        var ol = original.Lines.FirstOrDefault(l =>
                            l.Description.Replace(" (migreret)", "")
                                .Replace(" (migreret, abonnement)", "")
                                .StartsWith(normalDesc)) ??
                            // Also try matching from recalc → original by stripping charge details
                            original.Lines.FirstOrDefault(l =>
                                l.Description.Contains(normalDesc.Split(' ')[0]));
                        return new
                        {
                            description = rl.Description,
                            source = rl.Source.ToString(),
                            originalAmount = ol?.Amount.Amount,
                            originalDescription = ol?.Description,
                            recalcAmount = rl.Amount.Amount,
                            diff = ol is not null ? rl.Amount.Amount - ol.Amount.Amount : (decimal?)null,
                        };
                    }),
                } : null,
            });
        }).WithName("RecalculateSettlement");

        // ==================== SETTLEMENT ISSUES ====================

        app.MapGet("/api/settlement-issues", async (string? status, Guid? meteringPointId, WattsOnDbContext db) =>
        {
            var query = db.SettlementIssues.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<SettlementIssueStatus>(status, true, out var parsedStatus))
                query = query.Where(i => i.Status == parsedStatus);
            else
                query = query.Where(i => i.Status == SettlementIssueStatus.Open); // Default: open only

            if (meteringPointId.HasValue)
                query = query.Where(i => i.MeteringPointId == meteringPointId.Value);

            var issues = await query
                .OrderByDescending(i => i.CreatedAt)
                .Take(200)
                .Select(i => new
                {
                    i.Id,
                    i.MeteringPointId,
                    i.TimeSeriesId,
                    i.TimeSeriesVersion,
                    PeriodStart = i.Period.Start,
                    PeriodEnd = i.Period.End,
                    IssueType = i.IssueType.ToString(),
                    i.Message,
                    i.Details,
                    Status = i.Status.ToString(),
                    i.ResolvedAt,
                    i.CreatedAt,
                })
                .ToListAsync();

            return Results.Ok(issues);
        }).WithName("GetSettlementIssues");

        app.MapGet("/api/settlement-issues/count", async (WattsOnDbContext db) =>
        {
            var openCount = await db.SettlementIssues.CountAsync(i => i.Status == SettlementIssueStatus.Open);
            return Results.Ok(new { open = openCount });
        }).WithName("GetSettlementIssueCount");

        app.MapPost("/api/settlement-issues/{id:guid}/dismiss", async (Guid id, WattsOnDbContext db) =>
        {
            var issue = await db.SettlementIssues.FindAsync(id);
            if (issue is null) return Results.NotFound();

            issue.Dismiss();
            await db.SaveChangesAsync();

            return Results.Ok(new { issue.Id, Status = issue.Status.ToString(), issue.ResolvedAt });
        }).WithName("DismissSettlementIssue");

        return app;
    }
}
