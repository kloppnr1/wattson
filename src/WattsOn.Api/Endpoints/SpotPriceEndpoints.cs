using Microsoft.EntityFrameworkCore;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SpotPriceEndpoints
{
    public static WebApplication MapSpotPriceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/spot-prices", async (string? area, int? days, string? date, WattsOnDbContext db) =>
        {
            IQueryable<Domain.Entities.SpotPrice> query = db.SpotPrices;

            if (!string.IsNullOrEmpty(date) && DateTimeOffset.TryParse(date, out var parsedDate))
            {
                // Filter to a specific date (in Danish time — date string like "2026-02-19")
                // Convert to UTC range: date 00:00 CET = date-1 23:00 UTC
                var startUtc = new DateTimeOffset(parsedDate.Year, parsedDate.Month, parsedDate.Day, 0, 0, 0, TimeSpan.FromHours(1)).ToUniversalTime();
                var endUtc = startUtc.AddDays(1);
                query = query.Where(sp => sp.HourUtc >= startUtc && sp.HourUtc < endUtc);
            }
            else
            {
                var since = DateTimeOffset.UtcNow.AddDays(-(days ?? 7));
                query = query.Where(sp => sp.HourUtc >= since);
            }

            if (!string.IsNullOrEmpty(area))
                query = query.Where(sp => sp.PriceArea == area);

            var prices = await query
                .OrderBy(sp => sp.HourUtc)
                .Take(5000)
                .Select(sp => new
                {
                    sp.HourUtc,
                    sp.HourDk,
                    sp.PriceArea,
                    sp.SpotPriceDkkPerMwh,
                    sp.SpotPriceEurPerMwh,
                    spotPriceDkkPerKwh = sp.SpotPriceDkkPerMwh / 1000m
                }).ToListAsync();

            return Results.Ok(prices);
        }).WithName("GetSpotPrices");

        app.MapGet("/api/spot-prices/latest", async (WattsOnDbContext db) =>
        {
            // Get the latest price for each area
            var dk1 = await db.SpotPrices
                .Where(sp => sp.PriceArea == "DK1")
                .OrderByDescending(sp => sp.HourUtc)
                .FirstOrDefaultAsync();

            var dk2 = await db.SpotPrices
                .Where(sp => sp.PriceArea == "DK2")
                .OrderByDescending(sp => sp.HourUtc)
                .FirstOrDefaultAsync();

            var totalCount = await db.SpotPrices.CountAsync();

            return Results.Ok(new
            {
                totalRecords = totalCount,
                dk1 = dk1 == null ? null : new
                {
                    dk1.HourUtc,
                    dk1.HourDk,
                    dk1.SpotPriceDkkPerMwh,
                    dk1.SpotPriceEurPerMwh,
                    spotPriceDkkPerKwh = dk1.SpotPriceDkkPerMwh / 1000m
                },
                dk2 = dk2 == null ? null : new
                {
                    dk2.HourUtc,
                    dk2.HourDk,
                    dk2.SpotPriceDkkPerMwh,
                    dk2.SpotPriceEurPerMwh,
                    spotPriceDkkPerKwh = dk2.SpotPriceDkkPerMwh / 1000m
                }
            });
        }).WithName("GetLatestSpotPrices");

        return app;
    }
}
