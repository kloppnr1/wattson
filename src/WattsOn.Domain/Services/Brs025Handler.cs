using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-025 — Anmodning om måledata (Request Historical Metered Data).
/// Initiator-only: we request detailed time series data for a metering point.
///
/// Request: RSM-015/E23 → DataHub (DGL role)
/// Response: RSM-012 with metered data → inbox → routed to BRS-021 handler
/// (same time series format as regular metered data).
/// </summary>
public static class Brs025Handler
{
    public record MeteredDataRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request historical metered data for a specific metering point and date range.
    /// Uses RSM-015 with processType E23 (historical metered data).
    /// </summary>
    public static MeteredDataRequestResult RequestMeteredData(
        Gsrn gsrn,
        GlnNumber ourGln,
        DateTimeOffset startDate,
        DateTimeOffset endDate)
    {
        if (endDate <= startDate)
            throw new ArgumentException("End date must be after start date.", nameof(endDate));

        var process = BrsProcess.Create(
            ProcessType.MåledataAnmodning,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            startDate);

        var transactionId = $"BRS025-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Anmodning om måledata sendt til DataHub");
        process.MarkSubmitted(transactionId);

        var seriesFields = new Dictionary<string, object?>
        {
            ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
            ["start_DateAndOrTime.dateTime"] = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["end_DateAndOrTime.dateTime"] = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm015, "E23", ourGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-015",
            senderGln: ourGln.Value,
            receiverGln: CimDocumentBuilder.DataHubGln,
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-025");

        return new MeteredDataRequestResult(process, outbox);
    }

    /// <summary>
    /// DataHub rejected our metered data request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Metered data received — mark process as completed.
    /// The actual data is processed by BRS-021 handler (same time series format).
    /// </summary>
    public static void HandleDataReceived(BrsProcess process)
    {
        process.TransitionTo("DataReceived", "Måledata modtaget fra DataHub");
        process.MarkCompleted();
    }
}
