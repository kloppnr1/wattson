using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Entities;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Infrastructure.Services;

/// <summary>
/// Shared spot price fetch logic — used by both SpotPriceWorker (background)
/// and the admin API endpoint (manual trigger).
/// </summary>
public static class SpotPriceFetcher
{
    private const string BaseUrl = "https://api.energidataservice.dk/dataset/DayAheadPrices";

    public record FetchResult(int Inserted, int Updated, int RecordsReceived);

    /// <summary>
    /// Fetch spot prices from Energi Data Service and store them.
    /// Returns the number of records inserted.
    /// </summary>
    public static async Task<FetchResult> FetchAndStore(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        int days,
        CancellationToken ct)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        var url = $"{BaseUrl}?start={start}&filter={{\"PriceArea\":[\"DK1\",\"DK2\"]}}&sort=TimeUTC%20asc&limit=20000";

        logger.LogInformation("Fetching spot prices from {Start} for DK1/DK2", start);

        var response = await httpClient.GetFromJsonAsync<EdsResponse>(url, ct);
        if (response?.Records == null || response.Records.Count == 0)
        {
            logger.LogInformation("No spot price records for recent period");
            return new FetchResult(0, 0, 0);
        }

        logger.LogInformation("Received {Count} spot price records", response.Records.Count);
        return await UpsertRecords(scopeFactory, logger, response.Records, ct);
    }

    /// <summary>
    /// Fetch latest N days of spot prices (sorted desc). Used for initial backfill.
    /// </summary>
    public static async Task<FetchResult> FetchLatest(
        HttpClient httpClient,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        int days,
        CancellationToken ct)
    {
        var intervalsToFetch = days * 96 * 2;
        var url = $"{BaseUrl}?filter={{\"PriceArea\":[\"DK1\",\"DK2\"]}}&sort=TimeUTC%20desc&limit={intervalsToFetch}";

        logger.LogInformation("Fetching latest {Count} spot price records (backfill)", intervalsToFetch);

        var response = await httpClient.GetFromJsonAsync<EdsResponse>(url, ct);
        if (response?.Records == null || response.Records.Count == 0)
        {
            logger.LogWarning("No spot price records available from Energi Data Service");
            return new FetchResult(0, 0, 0);
        }

        logger.LogInformation("Received {Count} spot price records (latest: {Latest})",
            response.Records.Count, response.Records.First().TimeUTC);

        return await UpsertRecords(scopeFactory, logger, response.Records, ct);
    }

    public static async Task<FetchResult> UpsertRecords(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        List<EdsRecord> records,
        CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        var validRecords = records.Where(r => r.DayAheadPriceDKK != null).ToList();
        if (validRecords.Count == 0) return new FetchResult(0, 0, records.Count);

        var minTs = validRecords.Min(r => r.TimeUTC);
        var maxTs = validRecords.Max(r => r.TimeUTC);

        var existingKeys = await db.SpotPrices
            .Where(sp => sp.Timestamp >= minTs && sp.Timestamp <= maxTs)
            .Select(sp => new { sp.PriceArea, sp.Timestamp })
            .ToListAsync(ct);

        var existingSet = existingKeys.ToHashSet();

        var inserted = 0;
        foreach (var record in validRecords)
        {
            var key = new { record.PriceArea, Timestamp = record.TimeUTC };
            if (existingSet.Contains(key)) continue;

            var priceDkkPerKwh = record.DayAheadPriceDKK!.Value / 1000m;
            db.SpotPrices.Add(SpotPrice.Create(record.PriceArea, record.TimeUTC, priceDkkPerKwh));
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Inserted {Count} new spot prices across DK1/DK2", inserted);
        }
        else
        {
            logger.LogDebug("No new spot prices to insert — all up to date");
        }

        return new FetchResult(inserted, 0, validRecords.Count);
    }

    // DTOs for Energi Data Service
    public class EdsResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("records")]
        public List<EdsRecord> Records { get; set; } = new();
    }

    public class EdsRecord
    {
        [JsonPropertyName("TimeUTC")]
        public DateTimeOffset TimeUTC { get; set; }

        [JsonPropertyName("TimeDK")]
        public DateTimeOffset TimeDK { get; set; }

        [JsonPropertyName("PriceArea")]
        public string PriceArea { get; set; } = null!;

        [JsonPropertyName("DayAheadPriceDKK")]
        public decimal? DayAheadPriceDKK { get; set; }

        [JsonPropertyName("DayAheadPriceEUR")]
        public decimal? DayAheadPriceEUR { get; set; }
    }
}
