namespace WattsOn.Domain.Enums;

/// <summary>
/// Identifies the source of a settlement line item.
/// Used for correction matching and traceability.
/// </summary>
public enum SettlementLineSource
{
    /// <summary>DataHub charge (nettarif, systemtarif, etc.) — linked via PriceId</summary>
    DataHubCharge = 0,

    /// <summary>Wholesale spot price from Energi Data Service</summary>
    SpotPrice = 1,

    /// <summary>Supplier's own margin/markup</summary>
    SupplierMargin = 2,
}
