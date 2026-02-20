namespace WattsOn.Migration.Core.Models;

public class ExtractedSettlement
{
    public string Gsrn { get; set; } = null!;
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public string BillingLogNum { get; set; } = null!;
    public string HistKeyNumber { get; set; } = null!;
    
    // Reconstructed settlement totals
    public decimal TotalEnergyKwh { get; set; }
    
    // Electricity component (from CalculatedPrice × TimeValue)
    public decimal ElectricityAmountDkk { get; set; }
    
    // Spot component (from PowerExchangePrice × TimeValue)
    public decimal SpotAmountDkk { get; set; }
    
    // Margin = Electricity - Spot
    public decimal MarginAmountDkk { get; set; }
    
    // Tariff lines (from PriceElementRates)
    public List<ExtractedTariffLine> TariffLines { get; set; } = new();
    
    // Total of all components
    public decimal TotalAmountDkk { get; set; }
}

public class ExtractedTariffLine
{
    public string PartyChargeTypeId { get; set; } = null!;
    public string Description { get; set; } = null!;
    public decimal AmountDkk { get; set; }
    public decimal EnergyKwh { get; set; }
    public decimal AvgUnitPrice { get; set; }
    /// <summary>True if this is a fixed monthly charge (abonnement), not per-kWh</summary>
    public bool IsSubscription { get; set; }
}