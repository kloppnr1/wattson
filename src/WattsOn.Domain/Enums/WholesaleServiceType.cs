namespace WattsOn.Domain.Enums;

/// <summary>
/// Type of wholesale service (engrosydelse).
/// These are the components that make up the total electricity cost.
/// </summary>
public enum WholesaleServiceType
{
    /// <summary>Nettarif — grid tariff from DSO</summary>
    Nettarif = 1,

    /// <summary>Systemtarif — system tariff from TSO</summary>
    Systemtarif = 2,

    /// <summary>Transmissionstarif — transmission tariff from TSO</summary>
    Transmissionstarif = 3,

    /// <summary>Elafgift — electricity tax</summary>
    Elafgift = 4,

    /// <summary>Balancetarif — balance tariff</summary>
    Balancetarif = 5,

    /// <summary>Abonnement — subscription from DSO</summary>
    NetAbonnement = 6
}
