using WattsOn.Domain.Entities;

namespace WattsOn.Domain.Services;

/// <summary>
/// Pure logic for upserting supplier margins.
/// Used by both the API endpoint and the simulation — single source of truth.
/// </summary>
public static class SupplierMarginService
{
    public record UpsertResult(int Inserted, int Updated);

    /// <summary>
    /// Upsert supplier margins: insert new, update existing (matched by identity + timestamp).
    /// Mutates existing entities in-place for updates.
    /// </summary>
    public static UpsertResult Upsert(
        Guid supplierIdentityId,
        IReadOnlyList<(DateTimeOffset Timestamp, decimal PriceDkkPerKwh)> points,
        IDictionary<DateTimeOffset, SupplierMargin> existingByTimestamp,
        Action<SupplierMargin> addEntity)
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
                var entity = SupplierMargin.Create(supplierIdentityId, timestamp, price);
                addEntity(entity);
                inserted++;
            }
        }

        return new UpsertResult(inserted, updated);
    }
}
