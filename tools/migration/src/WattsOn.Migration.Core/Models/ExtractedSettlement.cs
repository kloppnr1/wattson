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
    
    // ── Provenance: hourly consumption lines from FlexBillingHistoryLine ──
    public List<HourlyLine> HourlyLines { get; set; } = new();
    
    // ── Provenance: which product/margin rate was active ──
    public string? ProductName { get; set; }
    public decimal? MarginRateDkkPerKwh { get; set; }
    public DateTime? MarginRateValidFrom { get; set; }
}

/// <summary>
/// One hour of consumption from FlexBillingHistoryLine — full provenance.
/// </summary>
public class HourlyLine
{
    public DateTimeOffset Timestamp { get; set; }
    public decimal Kwh { get; set; }
    public decimal SpotPriceDkkPerKwh { get; set; }
    public decimal CalculatedPriceDkkPerKwh { get; set; }
    public decimal SpotAmountDkk { get; set; }
    public decimal MarginAmountDkk { get; set; }
    public decimal ElectricityAmountDkk { get; set; }
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
    
    // ── Provenance: which rate row was used ──
    public TariffRateProvenance? RateProvenance { get; set; }
    
    // ── Provenance: hourly tariff calculation detail ──
    public List<HourlyTariffDetail> HourlyDetail { get; set; } = new();
}

/// <summary>
/// Traces exactly which PriceElementRates row was selected for a tariff.
/// </summary>
public class TariffRateProvenance
{
    /// <summary>Table: EXU_PRICEELEMENTRATES</summary>
    public string Table { get; set; } = "EXU_PRICEELEMENTRATES";
    public string PartyChargeTypeId { get; set; } = null!;
    public DateTime RateStartDate { get; set; }
    public bool IsHourly { get; set; }
    public decimal FlatRate { get; set; }
    /// <summary>24 hourly rates (Price1..Price24), null if flat</summary>
    public decimal[]? HourlyRates { get; set; }
    /// <summary>How many candidate rate rows existed for this charge (for context)</summary>
    public int CandidateRateCount { get; set; }
    /// <summary>"Most recent rate with StartDate &lt;= {forDate}" explains the selection</summary>
    public string SelectionRule { get; set; } = null!;
}

/// <summary>
/// Per-hour tariff calculation: kWh × rate = amount.
/// </summary>
public class HourlyTariffDetail
{
    public DateTimeOffset Timestamp { get; set; }
    public int Hour { get; set; }
    public decimal Kwh { get; set; }
    public decimal RateDkkPerKwh { get; set; }
    public decimal AmountDkk { get; set; }
}