namespace WattsOn.Domain.Enums;

/// <summary>
/// MeteringPointsart — category of metering point.
/// Maps to DataHub D05 code list.
/// </summary>
public enum MeteringPointCategory
{
    /// <summary>D01 — Physical metering point with a physical meter</summary>
    Fysisk = 1,

    /// <summary>D02 — Virtual metering point (calculated from other points)</summary>
    Virtuel = 2,

    /// <summary>D03 — Calculated metering point (net settlement group 6)</summary>
    Beregnet = 3
}
