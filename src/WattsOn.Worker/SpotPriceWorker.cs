using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Fetches day-ahead electricity spot prices from Energi Data Service (energidataservice.dk).
/// Dataset: DayAheadPrices (15-minute resolution since 2025).
/// Stores them as SpotPrice entities — one row per price area per timestamp.
/// Polls every hour for DK1 and DK2 price areas.
/// </summary>
public class SpotPriceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpotPriceWorker> _logger;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval = TimeSpan.FromHours(1);

    private const string BaseUrl = "https://api.energidataservice.dk/dataset/DayAheadPrices";

    public SpotPriceWorker(IServiceScopeFactory scopeFactory, ILogger<SpotPriceWorker> logger, IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("EnergiDataService");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpotPriceWorker waiting for database schema...");
        await WaitForSchema(stoppingToken);

        _logger.LogInformation("SpotPriceWorker starting — polling every {Interval}h", _pollInterval.TotalHours);

        // Initial load: backfill if empty
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
            var hasAny = await db.SpotPrices.AnyAsync(stoppingToken);

            if (!hasAny)
            {
                _logger.LogInformation("No spot prices — fetching latest 30 days from Energi Data Service");
                await FetchLatestSpotPrices(days: 30, stoppingToken);
            }
        }

        await FetchSpotPrices(days: 2, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, stoppingToken);

            try
            {
                await FetchSpotPrices(days: 2, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching spot prices");
            }
        }
    }

    private async Task WaitForSchema(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
                await db.SpotPrices.AnyAsync(ct);
                return;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        _logger.LogWarning("SpotPriceWorker: spot_prices table not ready after 60s — proceeding anyway");
    }

    private async Task FetchLatestSpotPrices(int days, CancellationToken ct)
    {
        var intervalsToFetch = days * 96 * 2; // 96 quarter-hours per day × 2 areas
        var url = $"{BaseUrl}?filter={{\"PriceArea\":[\"DK1\",\"DK2\"]}}&sort=TimeUTC%20desc&limit={intervalsToFetch}";

        _logger.LogInformation("Fetching latest {Count} spot price records (backfill)", intervalsToFetch);

        try
        {
            var response = await _httpClient.GetFromJsonAsync<EdsResponse>(url, ct);
            if (response?.Records == null || response.Records.Count == 0)
            {
                _logger.LogWarning("No spot price records available from Energi Data Service");
                return;
            }

            _logger.LogInformation("Received {Count} spot price records (latest: {Latest})",
                response.Records.Count, response.Records.First().TimeUTC);

            await UpsertSpotPrices(response.Records, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Energi Data Service for backfill");
        }
    }

    private async Task FetchSpotPrices(int days, CancellationToken ct)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-days).ToString("yyyy-MM-dd");
        var url = $"{BaseUrl}?start={start}&filter={{\"PriceArea\":[\"DK1\",\"DK2\"]}}&sort=TimeUTC%20asc&limit=20000";

        _logger.LogInformation("Fetching spot prices from {Start} for DK1/DK2", start);

        try
        {
            var response = await _httpClient.GetFromJsonAsync<EdsResponse>(url, ct);
            if (response?.Records == null || response.Records.Count == 0)
            {
                _logger.LogInformation("No spot price records for recent period");
                return;
            }

            _logger.LogInformation("Received {Count} spot price records", response.Records.Count);
            await UpsertSpotPrices(response.Records, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Energi Data Service — will retry next poll");
        }
    }

    private async Task UpsertSpotPrices(List<EdsRecord> records, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        var validRecords = records.Where(r => r.DayAheadPriceDKK != null).ToList();
        if (validRecords.Count == 0) return;

        // Get existing timestamps per area to avoid duplicates
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
            _logger.LogInformation("Inserted {Count} new spot prices across DK1/DK2", inserted);
        }
        else
        {
            _logger.LogDebug("No new spot prices to insert — all up to date");
        }
    }

    // DTOs for Energi Data Service
    private class EdsResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("records")]
        public List<EdsRecord> Records { get; set; } = new();
    }

    private class EdsRecord
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
