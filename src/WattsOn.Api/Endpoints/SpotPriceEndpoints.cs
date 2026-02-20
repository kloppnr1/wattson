using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SpotPriceEndpoints
{
    public static WebApplication MapSpotPriceEndpoints(this WebApplication app)
    {
        // ==================== SPOTPRISER ====================

        app.MapGet("/api/spot-prices", async (string? priceArea, string? date, int? days, WattsOnDbContext db) =>
        {
            IQueryable<SpotPrice> query = db.SpotPrices.AsNoTracking();

            if (!string.IsNullOrEmpty(priceArea))
                query = query.Where(sp => sp.PriceArea == priceArea);

            if (!string.IsNullOrEmpty(date) && DateTimeOffset.TryParse(date, out var parsedDate))
            {
                var startUtc = new DateTimeOffset(parsedDate.Year, parsedDate.Month, parsedDate.Day,
                    0, 0, 0, TimeSpan.FromHours(1)).ToUniversalTime();
                var endUtc = startUtc.AddDays(1);
                query = query.Where(sp => sp.Timestamp >= startUtc && sp.Timestamp < endUtc);
            }
            else
            {
                var since = DateTimeOffset.UtcNow.AddDays(-(days ?? 2));
                query = query.Where(sp => sp.Timestamp >= since);
            }

            var points = await query
                .OrderBy(sp => sp.Timestamp)
                .Select(sp => new { sp.PriceArea, sp.Timestamp, sp.PriceDkkPerKwh })
                .ToListAsync();

            // Pivot DK1/DK2 by timestamp
            var pivoted = points
                .GroupBy(p => p.Timestamp)
                .Select(g =>
                {
                    decimal? dk1 = null, dk2 = null;
                    foreach (var p in g)
                    {
                        if (p.PriceArea == "DK1") dk1 = p.PriceDkkPerKwh;
                        else if (p.PriceArea == "DK2") dk2 = p.PriceDkkPerKwh;
                    }
                    return new { hourUtc = g.Key, dk1, dk2 };
                })
                .OrderBy(r => r.hourUtc)
                .ToList();

            return Results.Ok(new { totalRecords = await db.SpotPrices.CountAsync(), rows = pivoted });
        }).WithName("GetSpotPrices2");

        app.MapGet("/api/spot-prices/latest", async (WattsOnDbContext db) =>
        {
            var totalRecords = await db.SpotPrices.CountAsync();
            if (totalRecords == 0)
                return Results.Ok(new { totalRecords = 0, dk1 = (object?)null, dk2 = (object?)null });

            var dk1 = await db.SpotPrices
                .Where(sp => sp.PriceArea == "DK1")
                .OrderByDescending(sp => sp.Timestamp)
                .Select(sp => new { hourUtc = sp.Timestamp, sp.PriceDkkPerKwh })
                .FirstOrDefaultAsync();

            var dk2 = await db.SpotPrices
                .Where(sp => sp.PriceArea == "DK2")
                .OrderByDescending(sp => sp.Timestamp)
                .Select(sp => new { hourUtc = sp.Timestamp, sp.PriceDkkPerKwh })
                .FirstOrDefaultAsync();

            return Results.Ok(new { totalRecords, dk1 = (object?)dk1, dk2 = (object?)dk2 });
        }).WithName("GetLatestSpotPrices2");

        /// <summary>
        /// Bulk upsert spot prices for a price area.
        /// Inserts new points, updates existing ones (matched by area + timestamp).
        /// Used by SpotPriceWorker (auto-fetch) and simulation.
        /// </summary>
        app.MapPost("/api/spot-prices", async (UpsertSpotPricesRequest req, WattsOnDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.PriceArea) || (req.PriceArea != "DK1" && req.PriceArea != "DK2"))
                return Results.BadRequest(new { error = "PriceArea must be DK1 or DK2" });

            if (req.Points == null || req.Points.Count == 0)
                return Results.BadRequest(new { error = "Points required" });

            var timestamps = req.Points.Select(p => p.Timestamp).ToList();
            var existing = await db.SpotPrices
                .Where(sp => sp.PriceArea == req.PriceArea && sp.Timestamp >= timestamps.Min() && sp.Timestamp <= timestamps.Max())
                .ToDictionaryAsync(sp => sp.Timestamp);

            var points = req.Points.Select(p => (p.Timestamp, p.PriceDkkPerKwh)).ToList();
            var result = SpotPriceService.Upsert(req.PriceArea, points, existing, e => db.SpotPrices.Add(e));

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                priceArea = req.PriceArea,
                inserted = result.Inserted,
                updated = result.Updated,
                total = result.Inserted + result.Updated,
            });
        }).WithName("UpsertSpotPrices");

        // ==================== LEVERANDØRMARGIN ====================

        app.MapGet("/api/supplier-margins", async (Guid? supplierProductId, WattsOnDbContext db) =>
        {
            IQueryable<SupplierMargin> query = db.SupplierMargins.AsNoTracking();

            if (supplierProductId.HasValue)
                query = query.Where(m => m.SupplierProductId == supplierProductId.Value);

            var rows = await query
                .OrderBy(m => m.ValidFrom)
                .Select(m => new { m.Id, m.SupplierProductId, m.ValidFrom, m.PriceDkkPerKwh })
                .ToListAsync();

            return Results.Ok(new { totalRecords = rows.Count, rows });
        }).WithName("GetSupplierMargins");

        /// <summary>
        /// Bulk upsert supplier margins for a supplier product.
        /// Inserts new rates, updates existing ones (matched by product + validFrom).
        /// Each entry represents a rate effective from that date until the next entry.
        /// </summary>
        app.MapPost("/api/supplier-margins", async (UpsertSupplierMarginsRequest req, WattsOnDbContext db) =>
        {
            var product = await db.SupplierProducts.FindAsync(req.SupplierProductId);
            if (product is null)
                return Results.BadRequest(new { error = "SupplierProduct not found" });

            if (req.Rates == null || req.Rates.Count == 0)
                return Results.BadRequest(new { error = "Rates required" });

            var validFroms = req.Rates.Select(r => r.ValidFrom).ToList();
            var existing = await db.SupplierMargins
                .Where(m => m.SupplierProductId == req.SupplierProductId && m.ValidFrom >= validFroms.Min() && m.ValidFrom <= validFroms.Max())
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
            });
        }).WithName("UpsertSupplierMargins");

        return app;
    }
}
