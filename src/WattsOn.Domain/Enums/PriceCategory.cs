namespace WattsOn.Domain.Enums;

/// <summary>
/// Classifies the role a price/charge plays in the Danish electricity settlement.
/// Used by SettlementValidator to verify all mandatory cost components are present —
/// independent of charge ID format (which differs between DataHub and simulation).
/// </summary>
public enum PriceCategory
{
    /// <summary>Unclassified / other charge</summary>
    Andet = 0,

    /// <summary>Wholesale spot price (from Energi Data Service / Nord Pool)</summary>
    SpotPris = 1,

    /// <summary>Grid company distribution tariff (nettarif)</summary>
    Nettarif = 2,

    /// <summary>Energinet system tariff</summary>
    Systemtarif = 3,

    /// <summary>Energinet transmission tariff</summary>
    Transmissionstarif = 4,

    /// <summary>Electricity tax (elafgift)</summary>
    Elafgift = 5,

    /// <summary>Energinet balance tariff</summary>
    Balancetarif = 6,

    /// <summary>Grid company subscription fee (net abonnement)</summary>
    NetAbonnement = 7,

    /// <summary>Supplier margin / mark-up</summary>
    Leverandørtillæg = 8,
}
