using Microsoft.EntityFrameworkCore;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SpotPriceEndpoints
{
    public static WebApplication MapSpotPriceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/spot-prices", async (string? area, int? days, WattsOnDbContext db) =>
        {
            var since = DateTimeOffset.UtcNow.AddDays(-(days ?? 7));
            var query = db.SpotPrices
                .Where(sp => sp.HourUtc >= since)
                .OrderByDescending(sp => sp.HourUtc)
                .AsQueryable();

            if (!string.IsNullOrEmpty(area))
                query = query.Where(sp => sp.PriceArea == area);

            var prices = await query.Take(1000).Select(sp => new
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
