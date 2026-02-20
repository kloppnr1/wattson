using Microsoft.EntityFrameworkCore;
using WattsOn.Infrastructure.Persistence;
using WattsOn.Infrastructure.Services;

namespace WattsOn.Api.Endpoints;

public static class AdminEndpoints
{
    public static WebApplication MapAdminEndpoints(this WebApplication app)
    {
        /// <summary>
        /// Trigger a one-off spot price fetch from Energi Data Service.
        /// Same logic as SpotPriceWorker but on-demand.
        /// </summary>
        app.MapPost("/api/admin/spot-price-fetch", async (int? days, IHttpClientFactory httpClientFactory,
            IServiceScopeFactory scopeFactory, ILoggerFactory loggerFactory) =>
        {
            var fetchDays = days ?? 7;
            var httpClient = httpClientFactory.CreateClient("EnergiDataService");
            var logger = loggerFactory.CreateLogger("SpotPriceFetcher");

            try
            {
                var result = await SpotPriceFetcher.FetchAndStore(httpClient, scopeFactory, logger, fetchDays, CancellationToken.None);
                return Results.Ok(new
                {
                    success = true,
                    daysFetched = fetchDays,
                    recordsReceived = result.RecordsReceived,
                    inserted = result.Inserted,
                    updated = result.Updated,
                });
            }
            catch (HttpRequestException ex)
            {
                return Results.Ok(new
                {
                    success = false,
                    error = $"Failed to reach Energi Data Service: {ex.Message}",
                    daysFetched = fetchDays,
                });
            }
        }).WithName("FetchSpotPrices");

        /// <summary>
        /// System stats for the admin page.
        /// </summary>
        app.MapGet("/api/admin/stats", async (WattsOnDbContext db) =>
        {
            var spotCount = await db.SpotPrices.CountAsync();
            var marginCount = await db.SupplierMargins.CountAsync();
            var priceCount = await db.Prices.CountAsync();
            var priceLinkCount = await db.PriceLinks.CountAsync();

            var latestSpot = await db.SpotPrices
                .OrderByDescending(sp => sp.Timestamp)
                .Select(sp => sp.Timestamp)
                .FirstOrDefaultAsync();

            var earliestSpot = await db.SpotPrices
                .OrderBy(sp => sp.Timestamp)
                .Select(sp => sp.Timestamp)
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                spotPrices = new
                {
                    count = spotCount,
                    earliest = earliestSpot == default ? null : (DateTimeOffset?)earliestSpot,
                    latest = latestSpot == default ? null : (DateTimeOffset?)latestSpot,
                },
                supplierMargins = new { count = marginCount },
                datahubCharges = new { count = priceCount, links = priceLinkCount },
            });
        }).WithName("GetAdminStats");

        return app;
    }
}
