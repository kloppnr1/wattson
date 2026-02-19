using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-023 — Fremsendelse af beregnede energitidsserier.
/// Processes RSM-014 messages with aggregated consumption/production per supplier/grid area.
/// Used for reconciliation against our own settlement calculations.
/// </summary>
public static class Brs023Handler
{
    public record AggregatedDataResult(AggregatedTimeSeries TimeSeries);

    public record AggregatedObservationData(DateTimeOffset Timestamp, decimal Kwh);

    public static AggregatedDataResult ProcessAggregatedData(
        string gridArea,
        string businessReason,
        string meteringPointType,
        string? settlementMethod,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Resolution resolution,
        string qualityStatus,
        string? transactionId,
        IReadOnlyList<AggregatedObservationData> observations)
    {
        var period = Period.Create(periodStart, periodEnd);
        var ts = AggregatedTimeSeries.Create(
            gridArea, businessReason, meteringPointType, settlementMethod,
            period, resolution, qualityStatus, transactionId);

        foreach (var obs in observations)
        {
            ts.AddObservation(obs.Timestamp, obs.Kwh);
        }

        return new AggregatedDataResult(ts);
    }

    public static string MapBusinessReasonToLabel(string code) => code switch
    {
        "D03" => "Foreløbig aggregering",
        "D04" => "Balancefiksering",
        "D05" => "Engrosfiksering",
        "D32" => "Korrektionsafregning",
        _ => code
    };
}
