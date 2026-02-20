using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Tracks which supplier product is active on a supply during which period.
/// A supply has one active product at any given time, but the product can change.
///
/// Mirrors Xellent's ExuContractPartTable: same contract (supply), different
/// product (PRODUCTNUM) over different time periods (STARTDATE/ENDDATE).
///
/// Example history:
///   2024-01-01 → 2024-06-30: "Spot Flex" (margin 5 øre/kWh)
///   2024-07-01 → open:       "Erhverv Fast" (margin 15 øre/kWh)
/// </summary>
public class SupplyProductPeriod : Entity
{
    /// <summary>The supply this product assignment belongs to</summary>
    public Guid SupplyId { get; private set; }

    /// <summary>The product active during this period</summary>
    public Guid SupplierProductId { get; private set; }

    /// <summary>Period during which this product is active on the supply [start, end)</summary>
    public Period Period { get; private set; } = null!;

    // Navigation
    public Supply Supply { get; private set; } = null!;
    public SupplierProduct SupplierProduct { get; private set; } = null!;

    private SupplyProductPeriod() { } // EF Core

    public static SupplyProductPeriod Create(
        Guid supplyId,
        Guid supplierProductId,
        Period period)
    {
        return new SupplyProductPeriod
        {
            SupplyId = supplyId,
            SupplierProductId = supplierProductId,
            Period = period,
        };
    }

    /// <summary>
    /// End this product period (when the customer switches to a new product).
    /// </summary>
    public void EndPeriod(DateTimeOffset endDate)
    {
        if (!Period.IsOpenEnded && Period.End <= endDate)
            throw new InvalidOperationException("Product period already ended before the specified date.");

        Period = Period.ClosedAt(endDate);
        MarkUpdated();
    }
}
