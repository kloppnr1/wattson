using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// AggregatedTimeSeries — aggregated energy data from DataHub (BRS-023).
/// Not per metering point, but per supplier/grid area combination.
/// Used for reconciliation against our own settlement calculations.
/// </summary>
public class AggregatedTimeSeries : Entity
{
    /// <summary>Grid area this data covers</summary>
    public string GridArea { get; private set; } = null!;

    /// <summary>Business reason: D03=Preliminary, D04=Balance, D05=Wholesale, D32=Correction</summary>
    public string BusinessReason { get; private set; } = null!;

    /// <summary>Metering point type: E17=Consumption, E18=Production</summary>
    public string MeteringPointType { get; private set; } = null!;

    /// <summary>Settlement method: E02=Hourly, D01=Flex (if applicable)</summary>
    public string? SettlementMethod { get; private set; }

    /// <summary>Period this time series covers</summary>
    public Period Period { get; private set; } = null!;

    /// <summary>Resolution of the data</summary>
    public Resolution Resolution { get; private set; }

    /// <summary>Total energy quantity in kWh</summary>
    public decimal TotalEnergyKwh { get; private set; }

    /// <summary>Quality status: Measured/Estimated/Incomplete</summary>
    public string QualityStatus { get; private set; } = null!;

    /// <summary>When received from DataHub</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>DataHub transaction ID</summary>
    public string? TransactionId { get; private set; }

    /// <summary>Individual data points</summary>
    private readonly List<AggregatedObservation> _observations = new();
    public IReadOnlyList<AggregatedObservation> Observations => _observations.AsReadOnly();

    private AggregatedTimeSeries() { } // EF Core

    public static AggregatedTimeSeries Create(
        string gridArea,
        string businessReason,
        string meteringPointType,
        string? settlementMethod,
        Period period,
        Resolution resolution,
        string qualityStatus,
        string? transactionId)
    {
        return new AggregatedTimeSeries
        {
            GridArea = gridArea,
            BusinessReason = businessReason,
            MeteringPointType = meteringPointType,
            SettlementMethod = settlementMethod,
            Period = period,
            Resolution = resolution,
            TotalEnergyKwh = 0,
            QualityStatus = qualityStatus,
            ReceivedAt = DateTimeOffset.UtcNow,
            TransactionId = transactionId
        };
    }

    public void AddObservation(DateTimeOffset timestamp, decimal kwh)
    {
        _observations.Add(AggregatedObservation.Create(Id, timestamp, kwh));
        TotalEnergyKwh += kwh;
    }
}

public class AggregatedObservation : Entity
{
    public Guid AggregatedTimeSeriesId { get; private set; }
    public DateTimeOffset Timestamp { get; private set; }
    public decimal Kwh { get; private set; }

    private AggregatedObservation() { }

    public static AggregatedObservation Create(Guid parentId, DateTimeOffset timestamp, decimal kwh)
    {
        return new AggregatedObservation
        {
            AggregatedTimeSeriesId = parentId,
            Timestamp = timestamp,
            Kwh = kwh
        };
    }
}
