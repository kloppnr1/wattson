using WattsOn.Domain.Common;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Spot price from Energi Data Service / Nord Pool.
/// One row per price area per timestamp interval (PT15M resolution).
/// Not a DataHub charge — fetched directly from the wholesale market.
/// </summary>
public class SpotPrice : Entity
{
    /// <summary>Price area: DK1 or DK2</summary>
    public string PriceArea { get; private set; } = null!;

    /// <summary>Start of the price interval (UTC)</summary>
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>Spot price in DKK per kWh</summary>
    public decimal PriceDkkPerKwh { get; private set; }

    private SpotPrice() { } // EF Core

    public static SpotPrice Create(string priceArea, DateTimeOffset timestamp, decimal priceDkkPerKwh)
    {
        return new SpotPrice
        {
            PriceArea = priceArea,
            Timestamp = timestamp,
            PriceDkkPerKwh = priceDkkPerKwh,
        };
    }

    public void UpdatePrice(decimal priceDkkPerKwh)
    {
        PriceDkkPerKwh = priceDkkPerKwh;
        MarkUpdated();
    }
}
