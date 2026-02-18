using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Observation — a single data point in a time series.
/// Stored in TimescaleDB hypertable for efficient time-range queries.
/// </summary>
public class Observation : Entity
{
    public Guid TimeSeriesId { get; private set; }

    /// <summary>Timestamp of this observation (start of interval)</summary>
    public DateTimeOffset Timestamp { get; private set; }

    /// <summary>Energy quantity for this interval</summary>
    public EnergyQuantity Quantity { get; private set; } = null!;

    /// <summary>Quality of this measurement</summary>
    public QuantityQuality Quality { get; private set; }

    private Observation() { } // EF Core

    public static Observation Create(
        Guid timeSeriesId,
        DateTimeOffset timestamp,
        EnergyQuantity quantity,
        QuantityQuality quality)
    {
        return new Observation
        {
            TimeSeriesId = timeSeriesId,
            Timestamp = timestamp,
            Quantity = quantity,
            Quality = quality
        };
    }
}
