using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Entities;

/// <summary>
/// A supplier product offering — e.g., "Spot Flex", "Erhverv Fast 12 mdr", "Grøn Variabel".
/// Defines the commercial product the supplier sells to customers.
/// The product determines the supplier margin (via SupplierMargin keyed to this product).
///
/// PricingModel determines how electricity cost is calculated in settlement:
/// - SpotAddon: spot price + flat margin addon per kWh
/// - Fixed: fixed price per kWh (margin IS the full electricity price)
///
/// Mirrors Xellent's InventTable/ExuProductExtendTable concept:
/// each product has a name and determines pricing terms.
/// </summary>
public class SupplierProduct : Entity
{
    /// <summary>Which supplier identity (GLN) offers this product</summary>
    public Guid SupplierIdentityId { get; private set; }

    /// <summary>Product name, e.g. "Spot Flex", "Erhverv Fast 12 mdr"</summary>
    public string Name { get; private set; } = null!;

    /// <summary>Human-readable description</summary>
    public string? Description { get; private set; }

    /// <summary>How this product determines electricity cost in settlement</summary>
    public PricingModel PricingModel { get; private set; } = PricingModel.SpotAddon;

    /// <summary>Whether this product is currently offered to new customers</summary>
    public bool IsActive { get; private set; } = true;

    // Navigation
    public SupplierIdentity SupplierIdentity { get; private set; } = null!;

    private SupplierProduct() { } // EF Core

    public static SupplierProduct Create(
        Guid supplierIdentityId,
        string name,
        PricingModel pricingModel = PricingModel.SpotAddon,
        string? description = null)
    {
        return new SupplierProduct
        {
            SupplierIdentityId = supplierIdentityId,
            Name = name,
            PricingModel = pricingModel,
            Description = description,
        };
    }

    public void UpdateInfo(string name, string? description)
    {
        Name = name;
        Description = description;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }

    public void Activate()
    {
        IsActive = true;
        MarkUpdated();
    }
}
