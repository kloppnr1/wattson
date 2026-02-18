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

        // Initial fetch on startup — get last 30 days to backfill
        await FetchSpotPrices(days: 30, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, stoppingToken);

            try
            {
                // Regular polling — just get last 2 days (catches any gaps)
                await FetchSpotPrices(days: 2, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching spot prices");
            }
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
                _logger.LogWarning("No spot price records returned from Energi Data Service");
                return;
            }

            _logger.LogInformation("Received {Count} spot price records", response.Records.Count);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

            var inserted = 0;
            foreach (var record in response.Records)
            {
                if (record.SpotPriceDKK == null) continue;

                var hourUtc = record.HourUTC;
                var area = record.PriceArea;

                // Check if we already have this price
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
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to reach Energi Data Service — will retry next poll");
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
