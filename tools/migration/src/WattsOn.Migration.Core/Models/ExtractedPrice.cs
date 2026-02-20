namespace WattsOn.Migration.Core.Models;

/// <summary>
/// A DataHub charge extracted from Xellent's PriceElementTable + PriceElementRates.
/// Maps to WattsOn Price entity (nettarif, systemtarif, transmissionstarif, elafgift, etc.)
/// </summary>
public class ExtractedPrice
{
    /// <summary>DataHub charge ID (PartyChargeTypeId in Xellent)</summary>
    public string ChargeId { get; set; } = null!;

    /// <summary>Owner GLN — grid operator or Energinet</summary>
    public string OwnerGln { get; set; } = null!;

    /// <summary>Tarif, Gebyr, or Abonnement</summary>
    public string Type { get; set; } = null!;

    /// <summary>Human-readable description from PriceElementTable</summary>
    public string Description { get; set; } = null!;

    /// <summary>Earliest effective date across all rate entries</summary>
    public DateTimeOffset EffectiveDate { get; set; }

    /// <summary>Price resolution (PT1H for hourly tariffs, null for flat)</summary>
    public string? Resolution { get; set; }

    /// <summary>Whether this is a tax (elafgift)</summary>
    public bool IsTax { get; set; }

    /// <summary>Whether this is a pass-through charge (system/transmission tariffs)</summary>
    public bool IsPassThrough { get; set; }

    /// <summary>Categorization for settlement grouping</summary>
    public string Category { get; set; } = "Andet";

    /// <summary>ChargeTypeCode from Xellent (3=tariff, 1=subscription, 2=fee)</summary>
    public int ChargeTypeCode { get; set; }

    /// <summary>Price points — flat or hourly rates per effective date</summary>
    public List<ExtractedPricePoint> Points { get; set; } = new();
}

public class ExtractedPricePoint
{
    public DateTimeOffset Timestamp { get; set; }
    public decimal Price { get; set; }
}
