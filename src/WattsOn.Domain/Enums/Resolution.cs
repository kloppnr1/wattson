namespace WattsOn.Domain.Enums;

/// <summary>
/// Time series resolution — the interval between observations.
/// </summary>
public enum Resolution
{
    /// <summary>15-minute intervals (quarter-hourly)</summary>
    PT15M = 1,

    /// <summary>1-hour intervals (hourly)</summary>
    PT1H = 2,

    /// <summary>1-day intervals (daily) — used for some aggregations</summary>
    P1D = 3,

    /// <summary>1-month intervals — used for settlement periods</summary>
    P1M = 4
}
