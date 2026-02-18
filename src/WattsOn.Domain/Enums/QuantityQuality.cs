namespace WattsOn.Domain.Enums;

/// <summary>
/// Quality of a metered data observation.
/// </summary>
public enum QuantityQuality
{
    /// <summary>A01 — Measured (aflæst)</summary>
    Målt = 1,

    /// <summary>A02 — Estimated by grid company (estimeret)</summary>
    Estimeret = 2,

    /// <summary>A03 — Calculated (beregnet)</summary>
    Beregnet = 3,

    /// <summary>A04 — Not available / missing</summary>
    IkkeTilgængelig = 4,

    /// <summary>A05 — Revised (revideret)</summary>
    Revideret = 5,

    /// <summary>E01 — Adjusted (korrigeret)</summary>
    Korrigeret = 6
}
