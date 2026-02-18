using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// TimeSeries — a versioned time series for a metering point.
/// Time series are NEVER overwritten — corrections create new versions.
/// Each version is a complete snapshot of the data for the period.
/// </summary>
public class TimeSeries : Entity
{
    public Guid MeteringPointId { get; private set; }

    /// <summary>The period this time series covers</summary>
    public Period Period { get; private set; } = null!;

    /// <summary>Resolution of the observations</summary>
    public Resolution Resolution { get; private set; }

    /// <summary>
    /// Version number — starts at 1, incremented for corrections.
    /// Higher version = more recent data for the same period.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Whether this is the latest version for its period</summary>
    public bool IsLatest { get; private set; }

    /// <summary>DataHub transaction ID that delivered this data</summary>
    public string? TransactionId { get; private set; }

    /// <summary>When this version was received from DataHub</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Individual observations in this time series</summary>
    private readonly List<Observation> _observations = new();
    public IReadOnlyList<Observation> Observations => _observations.AsReadOnly();

    // Navigation
    public MeteringPoint MeteringPoint { get; private set; } = null!;

    private TimeSeries() { } // EF Core

    public static TimeSeries Create(
        Guid meteringPointId,
        Period period,
        Resolution resolution,
        int version,
        string? transactionId = null)
    {
        return new TimeSeries
        {
            MeteringPointId = meteringPointId,
            Period = period,
            Resolution = resolution,
            Version = version,
            IsLatest = true,
            TransactionId = transactionId,
            ReceivedAt = DateTimeOffset.UtcNow
        };
    }

    public void AddObservation(DateTimeOffset timestamp, EnergyQuantity quantity, QuantityQuality quality)
    {
        _observations.Add(Observation.Create(Id, timestamp, quantity, quality));
    }

    public void AddObservations(IEnumerable<(DateTimeOffset Timestamp, EnergyQuantity Quantity, QuantityQuality Quality)> data)
    {
        foreach (var (timestamp, quantity, quality) in data)
        {
            AddObservation(timestamp, quantity, quality);
        }
    }

    /// <summary>Mark this version as superseded by a newer one</summary>
    public void Supersede()
    {
        IsLatest = false;
        MarkUpdated();
    }

    /// <summary>Total energy in this time series</summary>
    public EnergyQuantity TotalEnergy =>
        _observations.Aggregate(EnergyQuantity.Zero, (sum, obs) => sum + obs.Quantity);
}
