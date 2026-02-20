namespace WattsOn.Migration.Core.Models;

/// <summary>
/// Complete extracted dataset from Xellent for one migration run.
/// Serialized to JSON as a local cache — no VPN needed after extraction.
/// </summary>
public class ExtractedData
{
    public string[] AccountNumbers { get; set; } = [];
    public DateTimeOffset ExtractedAt { get; set; }
    public string SupplierGln { get; set; } = null!;
    public string SupplierName { get; set; } = null!;

    public List<ExtractedCustomer> Customers { get; set; } = new();
    public List<ExtractedProduct> Products { get; set; } = new();
    public List<ExtractedPrice> Prices { get; set; } = new();
    public List<ExtractedPriceLink> PriceLinks { get; set; } = new();
    public List<ExtractedTimeSeries> TimeSeries { get; set; } = new();
    public List<ExtractedSettlement> Settlements { get; set; } = new();

    /// <summary>Quick summary for logging</summary>
    public string Summary => $"""
        Extracted data for accounts: {string.Join(", ", AccountNumbers)}
          Customers:    {Customers.Count}
          MPs:          {Customers.Sum(c => c.MeteringPoints.Count)}
          Products:     {Products.Count}
          Prices:       {Prices.Count} charges ({Prices.Sum(p => p.Points.Count)} price points)
          Price Links:  {PriceLinks.Count}
          Time Series:  {TimeSeries.Count} ({TimeSeries.Sum(t => t.Observations.Count)} observations)
          Settlements:  {Settlements.Count}
        """;
}
