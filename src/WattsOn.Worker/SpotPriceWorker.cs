using Microsoft.EntityFrameworkCore;
using WattsOn.Infrastructure.Persistence;
using WattsOn.Infrastructure.Services;

namespace WattsOn.Worker;

/// <summary>
/// Background worker that polls Energi Data Service for spot prices.
/// Disabled by default — trigger manually via Admin page or POST /api/admin/spot-price-fetch.
/// Enable by uncommenting AddHostedService in Program.cs.
/// </summary>
public class SpotPriceWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpotPriceWorker> _logger;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval = TimeSpan.FromHours(1);

    public SpotPriceWorker(IServiceScopeFactory scopeFactory, ILogger<SpotPriceWorker> logger, IHttpClientFactory httpClientFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("EnergiDataService");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SpotPriceWorker waiting for database schema...");
        for (var attempt = 0; attempt < 30; attempt++)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
                await db.SpotPrices.AnyAsync(stoppingToken);
                break;
            }
            catch { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); }
        }

        _logger.LogInformation("SpotPriceWorker starting — polling every {Interval}h", _pollInterval.TotalHours);

        // Initial backfill if empty
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
            if (!await db.SpotPrices.AnyAsync(stoppingToken))
            {
                _logger.LogInformation("No spot prices — fetching latest 30 days");
                await SpotPriceFetcher.FetchLatest(_httpClient, _scopeFactory, _logger, 30, stoppingToken);
            }
        }

        await SpotPriceFetcher.FetchAndStore(_httpClient, _scopeFactory, _logger, 2, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_pollInterval, stoppingToken);
            try
            {
                await SpotPriceFetcher.FetchAndStore(_httpClient, _scopeFactory, _logger, 2, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching spot prices");
            }
        }
    }
}
