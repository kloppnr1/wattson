using WattsOn.Domain.Common;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Day-ahead electricity spot price from Nord Pool (via Energi Data Service).
/// Stored per price area (DK1, DK2) and time interval (15-min since 2025).
/// Prices are in DKK per MWh from the source — we store both DKK/MWh and convert to DKK/kWh.
/// </summary>
public class SpotPrice : Entity
{
    /// <summary>Hour in UTC</summary>
    public DateTimeOffset HourUtc { get; private set; }

    /// <summary>Hour in Danish time (CET/CEST)</summary>
    public DateTimeOffset HourDk { get; private set; }

    /// <summary>Price area: DK1 (West Denmark) or DK2 (East Denmark)</summary>
    public string PriceArea { get; private set; } = null!;

    /// <summary>Spot price in DKK per MWh (as received from Energi Data Service)</summary>
    public decimal SpotPriceDkkPerMwh { get; private set; }

    /// <summary>Spot price in EUR per MWh</summary>
    public decimal SpotPriceEurPerMwh { get; private set; }

    /// <summary>Spot price in DKK per kWh (= DKK/MWh / 1000)</summary>
    public decimal SpotPriceDkkPerKwh => SpotPriceDkkPerMwh / 1000m;

    private SpotPrice() { } // EF Core

    public static SpotPrice Create(
        DateTimeOffset hourUtc,
        DateTimeOffset hourDk,
        string priceArea,
        decimal spotPriceDkkPerMwh,
        decimal spotPriceEurPerMwh)
    {
        return new SpotPrice
        {
            HourUtc = hourUtc,
            HourDk = hourDk,
            PriceArea = priceArea,
            SpotPriceDkkPerMwh = spotPriceDkkPerMwh,
            SpotPriceEurPerMwh = spotPriceEurPerMwh,
        };
    }
}
