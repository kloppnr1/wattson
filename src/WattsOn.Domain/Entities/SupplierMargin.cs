using WattsOn.Domain.Common;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Supplier margin — the supplier's own markup per kWh.
/// One row per supplier identity per timestamp interval (PT1H or PT15M).
/// Not a DataHub charge — this is the supplier's business configuration.
/// </summary>
public class SupplierMargin : Entity
{
    /// <summary>Which supplier identity (GLN) this margin belongs to</summary>
    public Guid SupplierIdentityId { get; private set; }

    /// <summary>Start of the price interval (UTC)</summary>
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>Margin in DKK per kWh</summary>
    public decimal PriceDkkPerKwh { get; private set; }

    // Navigation
    public SupplierIdentity SupplierIdentity { get; private set; } = null!;

    private SupplierMargin() { } // EF Core

    public static SupplierMargin Create(Guid supplierIdentityId, DateTimeOffset timestamp, decimal priceDkkPerKwh)
    {
        return new SupplierMargin
        {
            SupplierIdentityId = supplierIdentityId,
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
