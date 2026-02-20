using WattsOn.Domain.Common;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Supplier margin — the supplier's own rate per kWh for a specific product.
/// One row per rate change (effective from ValidFrom until the next entry).
/// Not a DataHub charge — this is the supplier's business configuration.
///
/// For SpotAddon products: PriceDkkPerKwh is the margin addon on top of spot price.
/// For Fixed products: PriceDkkPerKwh IS the full electricity price.
///
/// Keyed to SupplierProduct (not SupplierIdentity) because different products
/// have different rates. E.g., "SMEspot" might be 0.04 DKK/kWh addon while
/// "Kvartal+" might be 0.85 DKK/kWh fixed.
///
/// Mirrors Xellent's ExuRateTable: product-specific rate that changes over time.
/// </summary>
public class SupplierMargin : Entity
{
    /// <summary>Which product this margin belongs to</summary>
    public Guid SupplierProductId { get; private set; }

    /// <summary>Date from which this rate is effective (UTC). Valid until the next entry.</summary>
    public DateTimeOffset ValidFrom { get; private set; }

    /// <summary>Rate in DKK per kWh</summary>
    public decimal PriceDkkPerKwh { get; private set; }

    // Navigation
    public SupplierProduct SupplierProduct { get; private set; } = null!;

    private SupplierMargin() { } // EF Core

    public static SupplierMargin Create(Guid supplierProductId, DateTimeOffset validFrom, decimal priceDkkPerKwh)
    {
        return new SupplierMargin
        {
            SupplierProductId = supplierProductId,
            ValidFrom = validFrom,
            PriceDkkPerKwh = priceDkkPerKwh,
        };
    }

    public void UpdatePrice(decimal priceDkkPerKwh)
    {
        PriceDkkPerKwh = priceDkkPerKwh;
        MarkUpdated();
    }
}
