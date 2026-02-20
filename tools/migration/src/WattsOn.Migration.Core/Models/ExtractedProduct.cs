namespace WattsOn.Migration.Core.Models;

/// <summary>
/// A supplier product extracted from Xellent (distinct ProductNum values).
/// </summary>
public class ExtractedProduct
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>
    /// Rate history from ExuRateTable — flat rate per start date.
    /// NOTE: Xellent stores a single rate per product/start date.
    /// We expand this into hourly SupplierMargin entries in WattsOn.
    /// TODO: Investigate whether Xellent has hourly rate differentiation
    /// for any products. If so, we need to extract hourly rates instead
    /// of expanding a flat rate. See ExuPriceElementRates (hours 1-24).
    /// </summary>
    public List<ExtractedRate> Rates { get; set; } = new();
}

public class ExtractedRate
{
    public DateTimeOffset StartDate { get; set; }
    public DateTimeOffset? EndDate { get; set; }
    public decimal RateDkkPerKwh { get; set; }
}
