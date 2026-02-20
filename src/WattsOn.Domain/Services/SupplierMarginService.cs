using WattsOn.Domain.Entities;

namespace WattsOn.Domain.Services;

/// <summary>
/// Pure logic for upserting supplier margins.
/// Used by both the API endpoint and the simulation — single source of truth.
/// Margins are keyed by SupplierProductId + ValidFrom (not hourly timestamps).
/// Each entry represents a rate effective from that date until the next entry.
/// </summary>
public static class SupplierMarginService
{
    public record UpsertResult(int Inserted, int Updated);

    /// <summary>
    /// Upsert supplier margins for a specific product: insert new, update existing (matched by product + validFrom).
    /// Mutates existing entities in-place for updates.
    /// </summary>
    public static UpsertResult Upsert(
        Guid supplierProductId,
        IReadOnlyList<(DateTimeOffset ValidFrom, decimal PriceDkkPerKwh)> rates,
        IDictionary<DateTimeOffset, SupplierMargin> existingByValidFrom,
        Action<SupplierMargin> addEntity)
    {
        var inserted = 0;
        var updated = 0;

        foreach (var (validFrom, price) in rates)
        {
            if (existingByValidFrom.TryGetValue(validFrom, out var existing))
            {
                existing.UpdatePrice(price);
                updated++;
            }
            else
            {
                var entity = SupplierMargin.Create(supplierProductId, validFrom, price);
                addEntity(entity);
                inserted++;
            }
        }

        return new UpsertResult(inserted, updated);
    }
}
