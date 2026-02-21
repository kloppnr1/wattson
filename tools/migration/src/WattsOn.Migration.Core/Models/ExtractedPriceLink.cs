namespace WattsOn.Migration.Core.Models;

/// <summary>
/// Price-to-metering-point link from Xellent (ExuDelpointPriceElementR25046).
/// </summary>
public class ExtractedPriceLink
{
    public string Gsrn { get; set; } = null!;
    public string ChargeId { get; set; } = null!;
    public string OwnerGln { get; set; } = null!;
    public DateTimeOffset EffectiveDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }

    /// <summary>
    /// Xellent ChargeTypeCode: 1=Abonnement, 2=Gebyr, 3=Tarif.
    /// Used to disambiguate when same (ChargeId, OwnerGln) has both tariff and subscription entries.
    /// </summary>
    public int ChargeTypeCode { get; set; }
}
