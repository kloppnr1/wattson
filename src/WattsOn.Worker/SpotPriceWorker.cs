using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Fetches hourly electricity spot prices from Energi Data Service (energidataservice.dk).
/// Polls every hour for DK1 and DK2 price areas.
/// Data source: Nord Pool via Energi Data Service (public, no API key required).
/// </summary>
public class SpotPriceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpotPriceWorker> _logger;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval = TimeSpan.FromHours(1);

    private const string BaseUrl = "https://api.energidataservice.dk/dataset/Elspotprices";

    public SpotPriceWorker(IServiceScopeFactory scopeFactory, ILogger<SpotPriceWorker> logger, IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("EnergiDataService");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpotPriceWorker starting — polling every {Interval}h", _pollInterval.TotalHours);

        // Check if DB is empty — if so, do a backfill
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
            var hasAny = await db.SpotPrices.AnyAsync(stoppingToken);
            if (!hasAny)
            {
                _logger.LogInformation("No spot prices in DB — fetching latest 30 days from Energi Data Service");
                await FetchLatestSpotPrices(days: 30, stoppingToken);
            }
        }

        // Also try the normal "recent" fetch
        await FetchSpotPrices(days: 30, stoppingToken);

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

    /// <summary>
    /// Fetch the latest available spot prices (sorted desc) regardless of system date.
    /// Used for initial backfill when the system date may be ahead of available data.
    /// </summary>
    private async Task FetchLatestSpotPrices(int days, CancellationToken ct)
    {
        var hoursToFetch = days * 24 * 2; // ×2 for DK1+DK2
        var url = $"{BaseUrl}?filter={{\"PriceArea\":[\"DK1\",\"DK2\"]}}&sort=HourUTC%20desc&limit={hoursToFetch}";

        _logger.LogInformation("Fetching latest {Hours} spot price records (backfill)", hoursToFetch);

        try
        {
            var response = await _httpClient.GetFromJsonAsync<EdsResponse>(url, ct);
            if (response?.Records == null || response.Records.Count == 0)
            {
                _logger.LogWarning("No spot price records available from Energi Data Service");
                return;
            }

            _logger.LogInformation("Received {Count} spot price records (latest: {Latest})",
                response.Records.Count, response.Records.First().HourUTC);

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
        var url = $"{BaseUrl}?start={start}&filter={{\"PriceArea\":[\"DK1\",\"DK2\"]}}&sort=HourUTC%20asc&limit=10000";

        _logger.LogInformation("Fetching spot prices from {Start} for DK1/DK2", start);

        try
        {
            var response = await _httpClient.GetFromJsonAsync<EdsResponse>(url, ct);
            if (response?.Records == null || response.Records.Count == 0)
            {
                _logger.LogInformation("No spot price records for recent period (data source may not have future dates)");
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

        var inserted = 0;
        foreach (var record in records)
        {
            if (record.SpotPriceDKK == null) continue;

            var hourUtc = record.HourUTC;
            var area = record.PriceArea;

            var exists = await db.SpotPrices
                .AnyAsync(sp => sp.HourUtc == hourUtc && sp.PriceArea == area, ct);

            if (exists) continue;

            var spotPrice = SpotPrice.Create(
                hourUtc: hourUtc,
                hourDk: record.HourDK,
                priceArea: area,
                spotPriceDkkPerMwh: record.SpotPriceDKK.Value,
                spotPriceEurPerMwh: record.SpotPriceEUR ?? 0);

            db.SpotPrices.Add(spotPrice);
            inserted++;
        }

        if (inserted > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Inserted {Count} new spot prices", inserted);
        }
        else
        {
            _logger.LogDebug("No new spot prices to insert — all up to date");
        }
    }

    // DTOs for Energi Data Service API response
    private class EdsResponse
    {
        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("records")]
        public List<EdsRecord> Records { get; set; } = new();
    }

    private class EdsRecord
    {
        [JsonPropertyName("HourUTC")]
        public DateTimeOffset HourUTC { get; set; }

        [JsonPropertyName("HourDK")]
        public DateTimeOffset HourDK { get; set; }

        [JsonPropertyName("PriceArea")]
        public string PriceArea { get; set; } = null!;

        [JsonPropertyName("SpotPriceDKK")]
        public decimal? SpotPriceDKK { get; set; }

        [JsonPropertyName("SpotPriceEUR")]
        public decimal? SpotPriceEUR { get; set; }
    }
}
