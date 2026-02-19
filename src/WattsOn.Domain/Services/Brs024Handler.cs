using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-024 — Anmodning om årssum (Request Yearly Consumption Sum).
/// Initiator-only: we request expected yearly consumption for a metering point.
///
/// Request: RSM-015/E30 → DataHub (DGL role)
/// Response: RSM-012 with yearly sum data → inbox
/// </summary>
public static class Brs024Handler
{
    public record YearlySumRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request yearly consumption sum for a specific metering point.
    /// Uses RSM-015 with processType E30 (yearly sum).
    /// </summary>
    public static YearlySumRequestResult RequestYearlySum(
        Gsrn gsrn,
        GlnNumber ourGln)
    {
        var process = BrsProcess.Create(
            ProcessType.ÅrssumAnmodning,
            ProcessRole.Initiator,
            "Created",
            gsrn);

        var transactionId = $"BRS024-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Anmodning om årssum sendt til DataHub");
        process.MarkSubmitted(transactionId);

        // Last 12 months
        var endDate = DateTimeOffset.UtcNow;
        var startDate = endDate.AddYears(-1);

        var seriesFields = new Dictionary<string, object?>
        {
            ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
            ["start_DateAndOrTime.dateTime"] = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["end_DateAndOrTime.dateTime"] = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm015, "E30", ourGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-015",
            senderGln: ourGln.Value,
            receiverGln: CimDocumentBuilder.DataHubGln,
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-024");

        return new YearlySumRequestResult(process, outbox);
    }

    /// <summary>
    /// DataHub rejected our yearly sum request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Yearly sum data received — mark process as completed.
    /// </summary>
    public static void HandleDataReceived(BrsProcess process)
    {
        process.TransitionTo("DataReceived", "Årssum data modtaget fra DataHub");
        process.MarkCompleted();
    }
}
