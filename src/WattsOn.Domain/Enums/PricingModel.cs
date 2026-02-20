namespace WattsOn.Domain.Enums;

/// <summary>
/// How a supplier product determines the electricity cost for settlement.
///
/// Fixed: customer pays a fixed price per kWh regardless of spot market.
///        SupplierMargin.PriceDkkPerKwh IS the full electricity price.
///
/// SpotAddon: customer pays spot price + a flat margin addon.
///            SupplierMargin.PriceDkkPerKwh is the addon on top of spot.
///
/// Mirrors Xellent's InventTable.ExuUseRateFromFlexPricing concept.
/// </summary>
public enum PricingModel
{
    /// <summary>Spot price + flat margin addon (e.g. "SMEspot", "V Variabel")</summary>
    SpotAddon = 0,

    /// <summary>Fixed price per kWh (e.g. "Kvartal+", "V P Fast")</summary>
    Fixed = 1,
}
