using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-005 — Anmodning om stamdata (Request Master Data).
/// Initiator-only: we request metering point master data from DataHub.
///
/// Request: RSM-020/E0G → DataHub (DDZ role)
/// Response: RSM-022 with master data → inbox → routed to BRS-006 handler
/// (same format as regular MP updates).
/// </summary>
public static class Brs005Handler
{
    public record MasterDataRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request master data for a specific metering point from DataHub.
    /// </summary>
    public static MasterDataRequestResult RequestMasterData(
        Gsrn gsrn,
        GlnNumber ourGln)
    {
        var process = BrsProcess.Create(
            ProcessType.StamdataAnmodning,
            ProcessRole.Initiator,
            "Created",
            gsrn);

        var transactionId = $"BRS005-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Anmodning om stamdata sendt til DataHub");
        process.MarkSubmitted(transactionId);

        var seriesFields = new Dictionary<string, object?>
        {
            ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
        };

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm020, "E0G", ourGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-020",
            senderGln: ourGln.Value,
            receiverGln: CimDocumentBuilder.DataHubGln,
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-005");

        return new MasterDataRequestResult(process, outbox);
    }

    /// <summary>
    /// DataHub rejected our master data request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Master data received — mark process as completed.
    /// The actual data is processed by BRS-006 handler (same RSM-022 format).
    /// </summary>
    public static void HandleDataReceived(BrsProcess process)
    {
        process.TransitionTo("DataReceived", "Stamdata modtaget fra DataHub");
        process.MarkCompleted();
    }
}
