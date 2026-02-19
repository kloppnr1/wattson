using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-039 — Serviceydelse (Service Request).
/// Initiator-only: we ask the grid company to perform a physical service
/// (disconnect, reconnect, or meter investigation).
///
/// Request: RSM-020 → DataHub (DDZ role)
/// Response: Accept/reject from DataHub (forwarded from grid company)
/// </summary>
public static class Brs039Handler
{
    /// <summary>Service type → DataHub process type code mapping.</summary>
    private static readonly Dictionary<string, string> ServiceTypeProcessCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Disconnect"] = "D09",
        ["Reconnect"] = "D10",
        ["MeterInvestigation"] = "D11",
    };

    public record ServiceRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request a physical service for a metering point.
    /// </summary>
    /// <param name="gsrn">Metering point GSRN</param>
    /// <param name="ourGln">Our GLN (sender)</param>
    /// <param name="serviceType">Service type: Disconnect, Reconnect, or MeterInvestigation</param>
    /// <param name="requestedDate">Requested date for the service</param>
    /// <param name="reason">Optional reason/notes for the service request</param>
    public static ServiceRequestResult RequestService(
        Gsrn gsrn,
        GlnNumber ourGln,
        string serviceType,
        DateTimeOffset requestedDate,
        string? reason = null)
    {
        if (!ServiceTypeProcessCodes.TryGetValue(serviceType, out var processCode))
            throw new ArgumentException(
                $"Unknown service type '{serviceType}'. Valid values: Disconnect, Reconnect, MeterInvestigation.",
                nameof(serviceType));

        var process = BrsProcess.Create(
            ProcessType.Serviceydelse,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            requestedDate);

        var transactionId = $"BRS039-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", $"Serviceanmodning ({serviceType}) sendt til DataHub");
        process.MarkSubmitted(transactionId);

        var seriesFields = new Dictionary<string, object?>
        {
            ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
            ["start_DateAndOrTime.dateTime"] = requestedDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["serviceType"] = serviceType,
        };

        if (reason is not null)
            seriesFields["reason"] = reason;

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm020, processCode, ourGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-020",
            senderGln: ourGln.Value,
            receiverGln: CimDocumentBuilder.DataHubGln,
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-039");

        return new ServiceRequestResult(process, outbox);
    }

    /// <summary>
    /// Service request accepted by grid company (via DataHub).
    /// </summary>
    public static void HandleAcceptance(BrsProcess process)
    {
        process.TransitionTo("Accepted", "Serviceanmodning godkendt");
        process.MarkConfirmed();
        process.TransitionTo("Completed", "Serviceydelse gennemført");
        process.MarkCompleted();
    }

    /// <summary>
    /// Service request rejected by grid company (via DataHub).
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }
}
