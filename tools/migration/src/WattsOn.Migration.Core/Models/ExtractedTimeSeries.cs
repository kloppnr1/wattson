namespace WattsOn.Migration.Core.Models;

/// <summary>
/// Time series data extracted from Xellent (EmsTimeseries + EmsTimeseriesValues).
/// </summary>
public class ExtractedTimeSeries
{
    public string Gsrn { get; set; } = null!;
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public string Resolution { get; set; } = "PT1H";
    public List<ExtractedObservation> Observations { get; set; } = new();
}

public class ExtractedObservation
{
    public DateTimeOffset Timestamp { get; set; }
    public decimal Kwh { get; set; }
    public string Quality { get; set; } = "A01"; // Measured
}
