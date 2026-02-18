using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-021 — Fremsendelse af måledata (Submission of Metered Data).
/// Processes RSM-012/E23 messages containing individual metering point readings.
/// Pure domain logic — no persistence, no side effects.
/// </summary>
public static class Brs021Handler
{
    public record MeteredDataResult(TimeSeries TimeSeries, TimeSeries? SupersededVersion);

    public record ObservationData(DateTimeOffset Timestamp, decimal Kwh, QuantityQuality Quality);

    /// <summary>
    /// Process incoming metered data for a metering point.
    /// Creates a new TimeSeries (or a new version if data for the same period already exists).
    /// </summary>
    public static MeteredDataResult ProcessMeteredData(
        Guid meteringPointId,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Resolution resolution,
        IReadOnlyList<ObservationData> observations,
        string? transactionId,
        TimeSeries? existingLatest)
    {
        // Determine version
        var version = 1;
        TimeSeries? superseded = null;

        if (existingLatest is not null)
        {
            existingLatest.Supersede();
            version = existingLatest.Version + 1;
            superseded = existingLatest;
        }

        var period = Period.Create(periodStart, periodEnd);
        var ts = TimeSeries.Create(meteringPointId, period, resolution, version, transactionId);

        foreach (var obs in observations)
        {
            ts.AddObservation(obs.Timestamp, EnergyQuantity.Create(obs.Kwh), obs.Quality);
        }

        return new MeteredDataResult(ts, superseded);
    }

    /// <summary>
    /// Map DataHub quantity status code to domain enum.
    /// Codes per ebIX / Danish DataHub RSM-012 specification:
    ///   A01 = Measured, A02 = Estimated, A03 = Calculated,
    ///   A04 = Not available, A05 = Revised, E01 = Adjusted.
    /// </summary>
    public static QuantityQuality MapQuantityStatus(string? statusCode)
    {
        return statusCode switch
        {
            null or "" => QuantityQuality.Measured,       // No status = measured
            "A01" => QuantityQuality.Measured,            // Measured
            "A02" => QuantityQuality.Estimated,           // Estimated by grid company
            "A03" => QuantityQuality.Calculated,          // Calculated
            "A04" => QuantityQuality.NotAvailable,        // Not available / missing
            "A05" => QuantityQuality.Revised,             // Revised
            "E01" => QuantityQuality.Adjusted,            // Adjusted
            _ => QuantityQuality.Estimated                // Default to estimated for unknown codes
        };
    }
}
