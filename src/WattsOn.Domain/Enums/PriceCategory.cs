namespace WattsOn.Domain.Enums;

/// <summary>
/// Classifies a DataHub charge's role in the Danish electricity settlement.
/// Only applies to prices received from DataHub via BRS-031/037.
/// Spot prices and supplier margins are separate entities — not DataHub charges.
/// </summary>
public enum PriceCategory
{
    /// <summary>Unclassified / other charge</summary>
    Andet = 0,

    /// <summary>Grid company distribution tariff (nettarif)</summary>
    Nettarif = 1,

    /// <summary>Energinet system tariff</summary>
    Systemtarif = 2,

    /// <summary>Energinet transmission tariff</summary>
    Transmissionstarif = 3,

    /// <summary>Electricity tax (elafgift)</summary>
    Elafgift = 4,

    /// <summary>Energinet balance tariff</summary>
    Balancetarif = 5,

    /// <summary>Grid company subscription fee (net abonnement)</summary>
    NetAbonnement = 6,
}
