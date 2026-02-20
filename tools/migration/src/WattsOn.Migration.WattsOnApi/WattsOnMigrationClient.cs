using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace WattsOn.Migration.WattsOnApi;

/// <summary>
/// HTTP client for WattsOn's /api/migration/* endpoints.
/// </summary>
public class WattsOnMigrationClient
{
    private readonly HttpClient _http;
    private readonly ILogger<WattsOnMigrationClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public WattsOnMigrationClient(HttpClient http, ILogger<WattsOnMigrationClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<JsonElement> PostAsync(string path, object payload)
    {
        var response = await _http.PostAsJsonAsync(path, payload, JsonOptions);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("POST {Path} failed ({Status}): {Body}", path, response.StatusCode, body);
            throw new HttpRequestException($"POST {path} failed ({response.StatusCode}): {body}");
        }

        return JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
    }

    // --- Convenience methods ---

    public Task<JsonElement> MigrateSupplierProducts(object payload)
        => PostAsync("/api/migration/supplier-products", payload);

    public Task<JsonElement> MigrateCustomers(object payload)
        => PostAsync("/api/migration/customers", payload);

    public Task<JsonElement> MigrateSupplyProductPeriods(object payload)
        => PostAsync("/api/migration/supply-product-periods", payload);

    public Task<JsonElement> MigrateSupplierMargins(object payload)
        => PostAsync("/api/migration/supplier-margins", payload);

    public Task<JsonElement> MigratePrices(object payload)
        => PostAsync("/api/migration/prices", payload);

    public Task<JsonElement> MigrateTimeSeries(object payload)
        => PostAsync("/api/migration/time-series", payload);

    public Task<JsonElement> MigratePriceLinks(object payload)
        => PostAsync("/api/migration/price-links", payload);

    public Task<JsonElement> MigrateSettlements(object payload)
        => PostAsync("/api/migration/settlements", payload);

    /// <summary>Get or create the supplier identity. Returns its ID.</summary>
    public async Task<Guid> EnsureSupplierIdentity(string gln, string name)
    {
        // Try to get existing
        var response = await _http.GetAsync("/api/supplier-identities");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in body.EnumerateArray())
        {
            if (item.GetProperty("gln").GetString() == gln)
                return item.GetProperty("id").GetGuid();
        }

        // Create new — handle duplicate gracefully (API may have seeded it on startup)
        var result = await _http.PostAsJsonAsync("/api/supplier-identities",
            new { gln, name, isActive = true }, JsonOptions);

        if (result.IsSuccessStatusCode)
        {
            var created = await result.Content.ReadFromJsonAsync<JsonElement>();
            return created.GetProperty("id").GetGuid();
        }

        // If creation failed (e.g. duplicate), retry GET
        _logger.LogWarning("POST supplier-identity failed ({Status}), retrying GET...", result.StatusCode);
        response = await _http.GetAsync("/api/supplier-identities");
        body = await response.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var item in body.EnumerateArray())
        {
            if (item.GetProperty("gln").GetString() == gln)
                return item.GetProperty("id").GetGuid();
        }

        throw new InvalidOperationException($"Could not ensure supplier identity for GLN {gln}");
    }
}
