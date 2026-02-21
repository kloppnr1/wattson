using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SettlementDocumentEndpoints
{
    public static WebApplication MapSettlementDocumentEndpoints(this WebApplication app)
    {
        /// <summary>
        /// Settlement documents ready for external invoicing system.
        /// Returns Peppol BIS-aligned pre-invoice documents with VAT, buyer/seller, and line items.
        /// Query by status: ready (new settlements), all, or corrections.
        /// </summary>
        app.MapGet("/api/settlement-documents", async (string? status, WattsOnDbContext db) =>
        {
            var query = db.Settlements
                .Include(a => a.Lines)
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                        .ThenInclude(c => c.SupplierIdentity)
                .AsNoTracking();

            // Filter: "ready" = Beregnet (default), "all" = everything
            if (status == "all")
            {
                // No filter
            }
            else if (status == "corrections")
            {
                query = query.Where(a => a.IsCorrection && a.Status == SettlementStatus.Calculated);
            }
            else // "ready" (default)
            {
                query = query.Where(a => a.Status == SettlementStatus.Calculated);
            }

            var settlements = await query
                .OrderByDescending(a => a.SettlementPeriod.Start)
                .ThenByDescending(a => a.DocumentNumber)
                .ToListAsync();

            // Load price VAT info for all referenced prices
            var priceIds = settlements.SelectMany(a => a.Lines).Where(l => l.PriceId.HasValue).Select(l => l.PriceId!.Value).Distinct().ToList();
            var prisVatMap = await db.Prices
                .Where(p => priceIds.Contains(p.Id))
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

            const decimal DanishVatRate = 25.0m;

            var documents = settlements.Select(a =>
            {
                var customer = a.Supply.Customer;
                var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
                var isDebit = a.IsCorrection && a.TotalAmount.Amount >= 0;

                var documentType = a.IsCorrection
                    ? (isCredit ? "creditNote" : "debitNote")
                    : "settlement";

                var year = a.CalculatedAt.Year;
                var documentId = $"WO-{year}-{a.DocumentNumber:D5}";
                string? originalDocumentId = null;
                if (a.PreviousSettlementId.HasValue)
                {
                    var original = settlements.FirstOrDefault(s => s.Id == a.PreviousSettlementId);
                    if (original is not null)
                        originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
                }

                var lines = a.Lines.Select((line, idx) =>
                {
                    var prisInfo = line.PriceId.HasValue ? prisVatMap.GetValueOrDefault(line.PriceId.Value) : null;
                    var vatExempt = prisInfo?.VatExempt ?? false;
                    var taxPercent = vatExempt ? 0m : DanishVatRate;
                    var taxAmount = vatExempt ? 0m : Math.Round(line.Amount.Amount * taxPercent / 100m, 2);

                    return new
                    {
                        lineNumber = idx + 1,
                        description = line.Description,
                        quantity = line.Quantity.Value,
                        quantityUnit = "KWH",
                        unitPrice = line.UnitPrice,
                        lineAmount = line.Amount.Amount,
                        chargeId = prisInfo?.ChargeId,
                        chargeOwnerGln = prisInfo?.OwnerGln,
                        taxCategory = vatExempt ? "Z" : "S", // S = Standard, Z = Zero-rated
                        taxPercent = taxPercent,
                        taxAmount = taxAmount
                    };
                }).ToList();

                var totalExclVat = a.TotalAmount.Amount;
                var totalVat = lines.Sum(l => l.taxAmount);
                var totalInclVat = totalExclVat + totalVat;

                // Group tax summary by category+rate
                var taxSummary = lines
                    .GroupBy(l => new { l.taxCategory, l.taxPercent })
                    .Select(g => new
                    {
                        taxCategory = g.Key.taxCategory,
                        taxPercent = g.Key.taxPercent,
                        taxableAmount = g.Sum(l => l.lineAmount),
                        taxAmount = g.Sum(l => l.taxAmount)
                    })
                    .ToList();

                return new
                {
                    documentType,
                    documentId,
                    originalDocumentId,
                    previousSettlementId = a.PreviousSettlementId,
                    settlementId = a.Id,
                    status = a.Status.ToString(),

                    period = new { start = a.SettlementPeriod.Start, end = a.SettlementPeriod.End },

                    seller = new
                    {
                        name = a.Supply.Customer.SupplierIdentity.Name,
                        identifier = a.Supply.Customer.SupplierIdentity.Cvr?.Value,
                        identifierScheme = "DK:CVR",
                        glnNumber = a.Supply.Customer.SupplierIdentity.Gln.Value
                    },
                    buyer = new
                    {
                        name = customer.Name,
                        identifier = customer.IsPrivate ? customer.Cpr?.Value : customer.Cvr?.Value,
                        identifierScheme = customer.IsPrivate ? "DK:CPR" : "DK:CVR",
                        address = customer.Address != null ? new
                        {
                            streetName = customer.Address.StreetName,
                            buildingNumber = customer.Address.BuildingNumber,
                            floor = customer.Address.Floor,
                            suite = customer.Address.Suite,
                            postCode = customer.Address.PostCode,
                            cityName = customer.Address.CityName
                        } : (object?)null
                    },

                    meteringPoint = new
                    {
                        gsrn = a.MeteringPoint.Gsrn.Value,
                        gridArea = a.MeteringPoint.GridArea
                    },

                    lines,
                    taxSummary,

                    totalExclVat,
                    totalVat,
                    totalInclVat,
                    currency = a.TotalAmount.Currency,

                    calculatedAt = a.CalculatedAt,
                    externalInvoiceReference = a.ExternalInvoiceReference,
                    invoicedAt = a.InvoicedAt
                };
            }).ToList();

            return Results.Ok(documents);
        }).WithName("GetSettlementDocuments");

        /// <summary>
        /// Get a single settlement document by ID.
        /// </summary>
        app.MapGet("/api/settlement-documents/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var a = await db.Settlements
                .Include(a => a.Lines)
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                        .ThenInclude(c => c.SupplierIdentity)
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id);

            if (a is null) return Results.NotFound();

            var priceIds = a.Lines.Where(l => l.PriceId.HasValue).Select(l => l.PriceId!.Value).Distinct().ToList();

            // Load prices WITH price points for line detail computation
            var prices = await db.Prices
                .Include(p => p.PricePoints)
                .Where(p => priceIds.Contains(p.Id))
                .AsNoTracking()
                .ToListAsync();

            var prisVatMap = prices.ToDictionary(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

            // Load observations for the settlement period (for tariff tier breakdowns)
            var periodEnd = a.SettlementPeriod.End ?? a.SettlementPeriod.Start.AddMonths(1);
            var timeSeries = await db.TimeSeriesCollection
                .Include(ts => ts.Observations.Where(o =>
                    o.Timestamp >= a.SettlementPeriod.Start && o.Timestamp < periodEnd))
                .Where(ts => ts.MeteringPointId == a.MeteringPointId)
                .Where(ts => ts.Observations.Any(o =>
                    o.Timestamp >= a.SettlementPeriod.Start && o.Timestamp < periodEnd))
                .AsNoTracking()
                .FirstOrDefaultAsync();

            var periodObs = timeSeries?.Observations.OrderBy(o => o.Timestamp).ToList()
                ?? new List<Observation>();

            // Load spot prices for the period
            var spotPrices = await db.SpotPrices
                .Where(sp => sp.PriceArea == a.MeteringPoint.GridArea)
                .Where(sp => sp.Timestamp >= a.SettlementPeriod.Start && sp.Timestamp < periodEnd)
                .OrderBy(sp => sp.Timestamp)
                .AsNoTracking()
                .ToListAsync();

            // Build PriceWithPoints (with cutoff for migrated settlements)
            var pointsCutoff = a.Status == SettlementStatus.Migrated
                ? a.SettlementPeriod.Start : (DateTimeOffset?)null;

            var priceWithPointsMap = prices.ToDictionary(
                p => p.Id,
                p => new PriceWithPoints(p, pointsCutoff));

            var dkTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");

            // ---- Per-line detail builder ----
            object? BuildLineDetails(SettlementLine line)
            {
                if (line.Source == SettlementLineSource.DataHubCharge)
                {
                    var isSubscription = line.Description.Contains("abonnement", StringComparison.OrdinalIgnoreCase);
                    if (!isSubscription && line.PriceId.HasValue
                        && priceWithPointsMap.TryGetValue(line.PriceId.Value, out var pwpFallback)
                        && pwpFallback.Price.Type == PriceType.Abonnement)
                        isSubscription = true;

                    if (isSubscription)
                    {
                        var days = (decimal)(periodEnd - a.SettlementPeriod.Start).TotalDays;
                        var dailyRate = line.Quantity.Value > 0 ? line.UnitPrice : 0m;
                        var totalAmount = line.Amount.Amount;
                        if (line.PriceId.HasValue && priceWithPointsMap.TryGetValue(line.PriceId.Value, out var pwpSub)
                            && pwpSub.Price.Type == PriceType.Abonnement)
                        {
                            var fromPrice = pwpSub.GetPriceAt(a.SettlementPeriod.Start);
                            if (fromPrice.HasValue) dailyRate = fromPrice.Value;
                        }
                        return new { type = "abonnement", days, dailyRate, totalAmount, daily = (object?)null };
                    }
                    else
                    {
                        PriceWithPoints? pwp = null;
                        if (line.PriceId.HasValue && priceWithPointsMap.TryGetValue(line.PriceId.Value, out var candidate)
                            && candidate.Price.Type == PriceType.Tarif)
                            pwp = candidate;

                        if (periodObs.Count == 0 || pwp is null)
                            return new { type = "tarif", totalHours = periodObs.Count, hoursWithPrice = 0,
                                tiers = Array.Empty<object>(), daily = Array.Empty<object>() };

                        var obsData = periodObs.Select(obs =>
                        {
                            var rate = pwp.GetPriceAt(obs.Timestamp) ?? 0m;
                            var kwh = obs.Quantity.Value;
                            var lt = TimeZoneInfo.ConvertTime(obs.Timestamp, dkTz);
                            return new { obs.Timestamp, Kwh = kwh, Rate = rate, Amount = kwh * rate, LocalDate = lt.Date, LocalHour = lt.Hour };
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

                        return new { type = "tarif", totalHours = periodObs.Count,
                            hoursWithPrice = obsData.Count(o => o.Rate > 0), tiers, daily };
                    }
                }

                if (line.Source == SettlementLineSource.SpotPrice)
                {
                    if (periodObs.Count == 0)
                        return new { type = "spot", totalHours = 0, hoursWithPrice = 0, hoursMissing = 0,
                            avgRate = 0m, minRate = 0m, maxRate = 0m, daily = Array.Empty<object>() };

                    var spotLookup = spotPrices.ToDictionary(s => s.Timestamp);
                    var obsData = periodObs.Select(obs =>
                    {
                        spotLookup.TryGetValue(obs.Timestamp, out var sp);
                        var rate = sp?.PriceDkkPerKwh ?? 0m;
                        var kwh = obs.Quantity.Value;
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
                    return new { type = "margin", ratePerKwh = line.UnitPrice, daily = (object?)null };

                return null;
            }

            const decimal DanishVatRate = 25.0m;

            var customer = a.Supply.Customer;
            var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
            var documentType = a.IsCorrection
                ? (isCredit ? "creditNote" : "debitNote")
                : "settlement";

            var year = a.CalculatedAt.Year;
            var documentId = $"WO-{year}-{a.DocumentNumber:D5}";

            string? originalDocumentId = null;
            Guid? previousSettlementId = a.PreviousSettlementId;
            if (a.PreviousSettlementId.HasValue)
            {
                var original = await db.Settlements.AsNoTracking()
                    .FirstOrDefaultAsync(o => o.Id == a.PreviousSettlementId);
                if (original is not null)
                    originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
            }

            // Find if an adjustment exists for this settlement
            var adjustment = await db.Settlements.AsNoTracking()
                .Where(adj => adj.PreviousSettlementId == a.Id)
                .Select(adj => new { adj.Id, adj.CalculatedAt, adj.DocumentNumber })
                .FirstOrDefaultAsync();

            string? adjustmentDocumentId = adjustment is not null
                ? $"WO-{adjustment.CalculatedAt.Year}-{adjustment.DocumentNumber:D5}"
                : null;

            var lines = a.Lines.Select((line, idx) =>
            {
                var prisInfo = line.PriceId.HasValue ? prisVatMap.GetValueOrDefault(line.PriceId.Value) : null;
                var vatExempt = prisInfo?.VatExempt ?? false;
                var taxPercent = vatExempt ? 0m : DanishVatRate;
                var taxAmount = vatExempt ? 0m : Math.Round(line.Amount.Amount * taxPercent / 100m, 2);

                return new
                {
                    lineNumber = idx + 1,
                    description = line.Description,
                    source = line.Source.ToString(),
                    quantity = line.Quantity.Value,
                    quantityUnit = line.Description.Contains("abonnement", StringComparison.OrdinalIgnoreCase)
                        ? "DAGE" : "KWH",
                    unitPrice = line.UnitPrice,
                    lineAmount = line.Amount.Amount,
                    chargeId = prisInfo?.ChargeId,
                    chargeOwnerGln = prisInfo?.OwnerGln,
                    taxCategory = vatExempt ? "Z" : "S",
                    taxPercent,
                    taxAmount,
                    details = BuildLineDetails(line),
                };
            }).ToList();

            var totalExclVat = a.TotalAmount.Amount;
            var totalVat = lines.Sum(l => l.taxAmount);
            var totalInclVat = totalExclVat + totalVat;

            var taxSummary = lines
                .GroupBy(l => new { l.taxCategory, l.taxPercent })
                .Select(g => new
                {
                    taxCategory = g.Key.taxCategory,
                    taxPercent = g.Key.taxPercent,
                    taxableAmount = g.Sum(l => l.lineAmount),
                    taxAmount = g.Sum(l => l.taxAmount)
                })
                .ToList();

            return Results.Ok(new
            {
                documentType,
                documentId,
                originalDocumentId,
                previousSettlementId,
                adjustmentSettlementId = adjustment?.Id,
                adjustmentDocumentId,
                settlementId = a.Id,
                status = a.Status.ToString(),

                period = new { start = a.SettlementPeriod.Start, end = a.SettlementPeriod.End },

                seller = new
                {
                    name = a.Supply.Customer.SupplierIdentity.Name,
                    identifier = a.Supply.Customer.SupplierIdentity.Cvr?.Value,
                    identifierScheme = "DK:CVR",
                    glnNumber = a.Supply.Customer.SupplierIdentity.Gln.Value
                },
                buyer = new
                {
                    name = customer.Name,
                    identifier = customer.IsPrivate ? customer.Cpr?.Value : customer.Cvr?.Value,
                    identifierScheme = customer.IsPrivate ? "DK:CPR" : "DK:CVR",
                    address = customer.Address != null ? new
                    {
                        streetName = customer.Address.StreetName,
                        buildingNumber = customer.Address.BuildingNumber,
                        floor = customer.Address.Floor,
                        suite = customer.Address.Suite,
                        postCode = customer.Address.PostCode,
                        cityName = customer.Address.CityName
                    } : (object?)null
                },

                meteringPoint = new
                {
                    gsrn = a.MeteringPoint.Gsrn.Value,
                    gridArea = a.MeteringPoint.GridArea
                },

                lines,
                taxSummary,

                totalExclVat,
                totalVat,
                totalInclVat,
                currency = a.TotalAmount.Currency,

                calculatedAt = a.CalculatedAt,
                externalInvoiceReference = a.ExternalInvoiceReference,
                invoicedAt = a.InvoicedAt
            });
        }).WithName("GetSettlementDocument");

        /// <summary>
        /// External system confirms a settlement document has been invoiced.
        /// Accepts the WattsOn document ID or settlement GUID.
        /// </summary>
        app.MapPost("/api/settlement-documents/{id:guid}/confirm", async (Guid id, ConfirmSettlementRequest req, WattsOnDbContext db) =>
        {
            var settlement = await db.Settlements.FindAsync(id);
            if (settlement is null) return Results.NotFound();

            try
            {
                settlement.MarkInvoiced(req.ExternalInvoiceReference);
                await db.SaveChangesAsync();

                var year = settlement.CalculatedAt.Year;
                return Results.Ok(new
                {
                    documentId = $"WO-{year}-{settlement.DocumentNumber:D5}",
                    settlementId = settlement.Id,
                    status = settlement.Status.ToString(),
                    externalInvoiceReference = settlement.ExternalInvoiceReference,
                    invoicedAt = settlement.InvoicedAt
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).WithName("ConfirmSettlementDocument");

        return app;
    }
}
