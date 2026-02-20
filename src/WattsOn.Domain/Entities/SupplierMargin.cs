using WattsOn.Domain.Common;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Supplier margin — the supplier's own markup per kWh for a specific product.
/// One row per product per timestamp interval (PT1H or PT15M).
/// Not a DataHub charge — this is the supplier's business configuration.
///
/// Keyed to SupplierProduct (not SupplierIdentity) because different products
/// have different margins. E.g., "Spot Flex" might be 5 øre/kWh while
/// "Erhverv Fast" might be 15 øre/kWh.
///
/// Mirrors Xellent's ExuRateTable: product-specific rate that changes over time.
/// </summary>
public class SupplierMargin : Entity
{
    /// <summary>Which product this margin belongs to</summary>
    public Guid SupplierProductId { get; private set; }

    /// <summary>Start of the price interval (UTC)</summary>
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>Margin in DKK per kWh</summary>
    public decimal PriceDkkPerKwh { get; private set; }

    // Navigation
    public SupplierProduct SupplierProduct { get; private set; } = null!;

    private SupplierMargin() { } // EF Core

    public static SupplierMargin Create(Guid supplierProductId, DateTimeOffset timestamp, decimal priceDkkPerKwh)
    {
        return new SupplierMargin
        {
            SupplierProductId = supplierProductId,
            Timestamp = timestamp,
            PriceDkkPerKwh = priceDkkPerKwh,
        };
    }

    public void UpdatePrice(decimal priceDkkPerKwh)
    {
        PriceDkkPerKwh = priceDkkPerKwh;
        MarkUpdated();
    }
}
