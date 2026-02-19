using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-036 — Ændring af aftagepligt (Product Obligation Change).
/// TSO changes product obligation on a production metering point.
/// Since MeteringPoint doesn't have a product obligation property,
/// we create an audit process to record the notification.
/// Pure domain logic — no persistence.
/// </summary>
public static class Brs036Handler
{
    public record ProductObligationData(
        Gsrn Gsrn,
        bool HasProductObligation,
        DateTimeOffset EffectiveDate);

    public record ProductObligationResult(
        BrsProcess Process,
        bool MeteringPointFound);

    /// <summary>
    /// Record a product obligation change notification.
    /// Creates an audit process with the obligation details stored as process data.
    /// </summary>
    public static ProductObligationResult RecordObligationChange(ProductObligationData data, bool meteringPointFound)
    {
        var process = BrsProcess.Create(
            ProcessType.AftagepligtÆndring,
            ProcessRole.Recipient,
            "Received",
            data.Gsrn,
            data.EffectiveDate);

        // Store the obligation details as process data
        var processData = JsonSerializer.Serialize(new
        {
            data.HasProductObligation,
            data.EffectiveDate,
            MeteringPointFound = meteringPointFound
        });
        process.SetProcessData(processData);

        if (!meteringPointFound)
        {
            process.TransitionTo("Completed", "Product obligation notification recorded (MP not found in our system)");
        }
        else
        {
            process.TransitionTo("Completed",
                $"Product obligation {(data.HasProductObligation ? "set" : "removed")} for GSRN {data.Gsrn}");
        }

        process.MarkCompleted();
        return new ProductObligationResult(process, meteringPointFound);
    }
}
