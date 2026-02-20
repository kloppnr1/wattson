namespace WattsOn.Migration.Core.Models;

/// <summary>
/// Result of a migration run.
/// </summary>
public class MigrationResult
{
    public int ProductsCreated { get; set; }
    public int ProductsSkipped { get; set; }
    public int CustomersCreated { get; set; }
    public int CustomersSkipped { get; set; }
    public int MeteringPointsCreated { get; set; }
    public int SuppliesCreated { get; set; }
    public int ProductPeriodsCreated { get; set; }
    public int ProductPeriodsSkipped { get; set; }
    public int MarginsCreated { get; set; }
    public int TimeSeriesCreated { get; set; }
    public int ObservationsCreated { get; set; }
    public int TimeSeriesSkipped { get; set; }
    public int PriceLinksCreated { get; set; }
    public int PriceLinksSkipped { get; set; }
    public int SettlementsCreated { get; set; }
    public int SettlementsSkipped { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public TimeSpan Duration { get; set; }

    public override string ToString()
    {
        return $"""
            Migration complete in {Duration.TotalSeconds:F1}s:
              Products:        {ProductsCreated} created, {ProductsSkipped} skipped
              Customers:       {CustomersCreated} created, {CustomersSkipped} skipped
              Metering Points: {MeteringPointsCreated} created
              Supplies:        {SuppliesCreated} created
              Product Periods: {ProductPeriodsCreated} created, {ProductPeriodsSkipped} skipped
              Margins:         {MarginsCreated} created
              Time Series:     {TimeSeriesCreated} created ({ObservationsCreated} observations), {TimeSeriesSkipped} skipped
              Price Links:     {PriceLinksCreated} created, {PriceLinksSkipped} skipped
              Settlements:     {SettlementsCreated} created, {SettlementsSkipped} skipped
              Errors:          {Errors.Count}
              Warnings:        {Warnings.Count}
            """;
    }
}
