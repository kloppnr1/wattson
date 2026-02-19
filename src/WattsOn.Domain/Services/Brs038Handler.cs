using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-038 — Anmodning om pristilknytninger (Request for Charge Links).
/// Initiator-only: we request which prices/charges are linked to a metering point.
///
/// Request: RSM-032/E0G → DataHub
/// Response: RSM-031/E0G (same format as BRS-037 price link updates) → inbox
///
/// Used for initial sync and on-demand refresh of price link data.
/// </summary>
public static class Brs038Handler
{
    public record ChargeLinkRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request charge links for a specific metering point from DataHub.
    /// </summary>
    public static ChargeLinkRequestResult RequestChargeLinks(
        Gsrn gsrn,
        GlnNumber ourGln,
        DateTimeOffset startDate,
        DateTimeOffset? endDate = null)
    {
        var process = BrsProcess.Create(
            ProcessType.PristilknytningAnmodning,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            startDate);

        var transactionId = $"BRS038-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Anmodning om pristilknytninger sendt til DataHub");
        process.MarkSubmitted(transactionId);

        var seriesFields = new Dictionary<string, object?>
        {
            ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
            ["start_DateAndOrTime.dateTime"] = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        if (endDate.HasValue)
            seriesFields["end_DateAndOrTime.dateTime"] = endDate.Value.ToString("yyyy-MM-ddTHH:mm:ssZ");

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm032, "E0G", ourGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-032",
            senderGln: ourGln.Value,
            receiverGln: "5790001330552",
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-038");

        return new ChargeLinkRequestResult(process, outbox);
    }

    /// <summary>
    /// DataHub rejected our charge link request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Charge link data received — mark process as completed.
    /// The actual link data is processed by BRS-031 handler (D17 format).
    /// </summary>
    public static void HandleDataReceived(BrsProcess process)
    {
        process.TransitionTo("DataReceived", "Pristilknytningsdata modtaget fra DataHub");
        process.MarkCompleted();
    }
}
