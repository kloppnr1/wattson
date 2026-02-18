namespace WattsOn.Domain.Enums;

/// <summary>
/// Quality of a metered data observation.
/// </summary>
public enum QuantityQuality
{
    /// <summary>A01 — Measured</summary>
    Measured = 1,

    /// <summary>A02 — Estimated by grid company</summary>
    Estimated = 2,

    /// <summary>A03 — Calculated</summary>
    Calculated = 3,

    /// <summary>A04 — Not available / missing</summary>
    NotAvailable = 4,

    /// <summary>A05 — Revised</summary>
    Revised = 5,

    /// <summary>E01 — Adjusted</summary>
    Adjusted = 6
}
