using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Fetches day-ahead electricity spot prices from Energi Data Service (energidataservice.dk).
/// Dataset: DayAheadPrices (15-minute resolution since 2025).
/// Stores them as Price entities (SPOT-DK1, SPOT-DK2) with PricePoints — same model as all other tariffs.
/// Polls every hour for DK1 and DK2 price areas.
/// </summary>
public class SpotPriceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpotPriceWorker> _logger;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval = TimeSpan.FromHours(1);

    private const string BaseUrl = "https://api.energidataservice.dk/dataset/DayAheadPrices";
    private const string SpotOwnerGln = "5790000432752"; // Energinet (market operator)

    public SpotPriceWorker(IServiceScopeFactory scopeFactory, ILogger<SpotPriceWorker> logger, IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("EnergiDataService");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for migrations to complete (API runs them on startup)
        _logger.LogInformation("SpotPriceWorker waiting for database schema...");
        await WaitForSchema(stoppingToken);

        _logger.LogInformation("SpotPriceWorker starting — polling every {Interval}h", _pollInterval.TotalHours);

        // Initial load
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
            var spotDk1 = await GetOrCreateSpotPrice(db, "DK1");
            var spotDk2 = await GetOrCreateSpotPrice(db, "DK2");
            var hasPricePoints = spotDk1.PricePoints.Any() || spotDk2.PricePoints.Any();

            if (!hasPricePoints)
            {
                _logger.LogInformation("No spot price points — fetching latest 30 days from Energi Data Service");
                await FetchLatestSpotPrices(days: 30, stoppingToken);
            }
        }

        // Also try normal recent fetch
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

    /// <summary>
    /// Wait for the prices table to exist (API applies migrations on startup).
    /// </summary>
    private async Task WaitForSchema(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
                await db.Prices.AnyAsync(ct);
                return; // Table exists
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        _logger.LogWarning("SpotPriceWorker: prices table not ready after 60s — proceeding anyway");
    }

    /// <summary>
    /// Get or create the Price entity for a spot price area.
    /// </summary>
    private async Task<Price> GetOrCreateSpotPrice(WattsOnDbContext db, string area)
    {
        var chargeId = $"SPOT-{area}";
        var existing = await db.Prices
            .Include(p => p.PricePoints)
            .FirstOrDefaultAsync(p => p.ChargeId == chargeId && p.OwnerGln.Value == SpotOwnerGln);

        if (existing != null) return existing;

        var price = Price.Create(
            chargeId: chargeId,
            ownerGln: GlnNumber.Create(SpotOwnerGln),
            type: PriceType.Tarif,
            description: $"Spotpris — {area} (Day-Ahead, Nord Pool)",
            validityPeriod: Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            vatExempt: false,
            priceResolution: Resolution.PT15M,
            isTax: false,
            isPassThrough: true,
            category: PriceCategory.SpotPris);

        db.Prices.Add(price);
        await db.SaveChangesAsync();

        _logger.LogInformation("Created spot price entity: {ChargeId}", chargeId);
        return price;
    }

    /// <summary>
    /// Fetch the latest available spot prices (sorted desc) regardless of system date.
    /// Used for initial backfill when the DB is empty.
    /// </summary>
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

        // Group records by price area
        var byArea = records
            .Where(r => r.DayAheadPriceDKK != null)
            .GroupBy(r => r.PriceArea);

        var totalInserted = 0;

        foreach (var areaGroup in byArea)
        {
            var area = areaGroup.Key;
            var price = await GetOrCreateSpotPrice(db, area);

            // Get existing timestamps for this price to avoid duplicates
            var newTimestamps = areaGroup.Select(r => r.TimeUTC).ToList();
            var minTs = newTimestamps.Min();
            var maxTs = newTimestamps.Max();

            var existingTimestamps = await db.PricePoints
                .Where(pp => pp.PriceId == price.Id && pp.Timestamp >= minTs && pp.Timestamp <= maxTs)
                .Select(pp => pp.Timestamp)
                .ToHashSetAsync(ct);

            var inserted = 0;
            foreach (var record in areaGroup)
            {
                if (existingTimestamps.Contains(record.TimeUTC)) continue;

                // Store as DKK/kWh (source is DKK/MWh)
                var pricePerKwh = record.DayAheadPriceDKK!.Value / 1000m;
                price.AddPricePoint(record.TimeUTC, pricePerKwh);
                inserted++;
            }

            totalInserted += inserted;

            if (inserted > 0)
            {
                // Update validity period to cover all data
                var earliestPoint = minTs < price.ValidityPeriod.Start ? minTs : price.ValidityPeriod.Start;
                price.UpdateValidity(Period.From(earliestPoint));
            }
        }

        if (totalInserted > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Inserted {Count} new spot price points across DK1/DK2", totalInserted);
        }
        else
        {
            _logger.LogDebug("No new spot prices to insert — all up to date");
        }
    }

    // DTOs for Energi Data Service DayAheadPrices API response
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
