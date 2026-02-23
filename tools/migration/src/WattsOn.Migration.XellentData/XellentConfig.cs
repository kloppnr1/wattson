namespace WattsOn.Migration.XellentData;

/// <summary>
/// Configuration for Xellent data extraction. Different COMPANYID values
/// correspond to different supplier GLNs (brands).
/// A single GLN may have multiple COMPANYID values (e.g. Aars Nibe Handel uses hni, vhe under DATAAREAID "han").
/// </summary>
public record XellentConfig
{
    public string DataAreaId { get; init; } = "hol";
    public string[] CompanyIds { get; init; } = ["for"];
    public string DeliveryCategory { get; init; } = "El-ekstern";
}
