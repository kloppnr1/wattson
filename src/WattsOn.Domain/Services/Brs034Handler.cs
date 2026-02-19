using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-034 — Anmodning om priser (Request for Prices).
/// Initiator-only: we request price data from DataHub.
///
/// Two request types:
/// - E0G: Price information (stamdata) — charge metadata
/// - D48: Price series — actual price points
///
/// Response comes back as RSM-034 through the inbox → routed to BRS-031 handler
/// (same D18/D08 format as regular price updates).
/// </summary>
public static class Brs034Handler
{
    public record PriceRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request price information (stamdata) from DataHub.
    /// Business reason E0G.
    /// </summary>
    public static PriceRequestResult RequestPriceInformation(
        GlnNumber ourGln,
        DateTimeOffset startDate,
        DateTimeOffset? endDate = null,
        string? priceOwnerGln = null,
        string? priceType = null,
        string? chargeId = null)
    {
        return CreateRequest(ourGln, "E0G", startDate, endDate, priceOwnerGln, priceType, chargeId);
    }

    /// <summary>
    /// Request price series (actual price points) from DataHub.
    /// Business reason D48.
    /// </summary>
    public static PriceRequestResult RequestPriceSeries(
        GlnNumber ourGln,
        DateTimeOffset startDate,
        DateTimeOffset? endDate = null,
        string? priceOwnerGln = null,
        string? priceType = null,
        string? chargeId = null)
    {
        return CreateRequest(ourGln, "D48", startDate, endDate, priceOwnerGln, priceType, chargeId);
    }

    private static PriceRequestResult CreateRequest(
        GlnNumber ourGln,
        string businessReason,
        DateTimeOffset startDate,
        DateTimeOffset? endDate,
        string? priceOwnerGln,
        string? priceType,
        string? chargeId)
    {
        var process = BrsProcess.Create(
            ProcessType.PrisAnmodning,
            ProcessRole.Initiator,
            "Created",
            effectiveDate: startDate);

        var transactionId = $"BRS034-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        var requestType = businessReason == "E0G" ? "prisoplysninger" : "prisserier";
        process.TransitionTo("Submitted", $"Anmodning om {requestType} sendt til DataHub");
        process.MarkSubmitted(transactionId);

        var payloadObj = new Dictionary<string, object?>
        {
            ["businessReason"] = businessReason,
            ["startDate"] = startDate,
            ["endDate"] = endDate,
        };

        if (priceOwnerGln is not null) payloadObj["priceOwnerGln"] = priceOwnerGln;
        if (priceType is not null) payloadObj["priceType"] = priceType;
        if (chargeId is not null) payloadObj["chargeId"] = chargeId;

        var payload = JsonSerializer.Serialize(payloadObj);

        var outbox = OutboxMessage.Create(
            documentType: "RSM-035",
            senderGln: ourGln.Value,
            receiverGln: "5790001330552",
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-034");

        return new PriceRequestResult(process, outbox);
    }

    /// <summary>
    /// DataHub rejected our price request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Price data received — mark process as completed.
    /// The actual price data is processed by BRS-031 handler (same D18/D08 format).
    /// </summary>
    public static void HandleDataReceived(BrsProcess process)
    {
        process.TransitionTo("DataReceived", "Prisdata modtaget fra DataHub");
        process.MarkCompleted();
    }
}
