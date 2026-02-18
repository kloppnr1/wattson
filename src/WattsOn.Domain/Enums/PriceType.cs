namespace WattsOn.Domain.Enums;

/// <summary>
/// Type of price/charge.
/// Maps to DataHub charge types.
/// </summary>
public enum PriceType
{
    /// <summary>D01 — Abonnement (subscription, fixed monthly fee)</summary>
    Abonnement = 1,

    /// <summary>D02 — Tarif (tariff, per-kWh charge)</summary>
    Tarif = 2,

    /// <summary>D03 — Gebyr (one-time fee)</summary>
    Gebyr = 3
}
