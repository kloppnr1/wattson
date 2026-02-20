using WattsOn.Domain.Entities;

namespace WattsOn.Domain.Services;

/// <summary>
/// Pure logic for upserting spot prices.
/// Used by both the API endpoint and the simulation — single source of truth.
/// </summary>
public static class SpotPriceService
{
    public record UpsertResult(int Inserted, int Updated);

    /// <summary>
    /// Upsert spot prices: insert new, update existing (matched by area + timestamp).
    /// Returns the entities to add (caller persists).
    /// Mutates existing entities in-place for updates.
    /// </summary>
    public static UpsertResult Upsert(
        string priceArea,
        IReadOnlyList<(DateTimeOffset Timestamp, decimal PriceDkkPerKwh)> points,
        IDictionary<DateTimeOffset, SpotPrice> existingByTimestamp,
        Action<SpotPrice> addEntity)
    {
        var inserted = 0;
        var updated = 0;

        foreach (var (timestamp, price) in points)
        {
            if (existingByTimestamp.TryGetValue(timestamp, out var existing))
            {
                existing.UpdatePrice(price);
                updated++;
            }
            else
            {
                var entity = SpotPrice.Create(priceArea, timestamp, price);
                addEntity(entity);
                inserted++;
            }
        }

        return new UpsertResult(inserted, updated);
    }
}
