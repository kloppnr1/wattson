namespace WattsOn.Migration.Core.Models;

/// <summary>
/// A customer extracted from Xellent, ready for mapping to WattsOn.
/// </summary>
public class ExtractedCustomer
{
    public string AccountNumber { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Cpr { get; set; }
    public string? Cvr { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public List<ExtractedMeteringPoint> MeteringPoints { get; set; } = new();
}

public class ExtractedMeteringPoint
{
    public string Gsrn { get; set; } = null!;
    /// <summary>Xellent METERINGPOINT column value (may differ from GSRN)</summary>
    public string? XellentMeteringPoint { get; set; }
    public string GridArea { get; set; } = null!; // DK1/DK2
    public string? GridOperatorGln { get; set; }
    public string SettlementMethod { get; set; } = "Flex";
    public DateTimeOffset SupplyStart { get; set; }
    public DateTimeOffset? SupplyEnd { get; set; }

    /// <summary>Product history on this metering point (from ContractParts)</summary>
    public List<ExtractedProductPeriod> ProductPeriods { get; set; } = new();
}

public class ExtractedProductPeriod
{
    public string ProductName { get; set; } = null!;
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset? End { get; set; }
}
