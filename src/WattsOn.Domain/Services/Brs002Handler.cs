using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-002 — Leveranceophør (End of Supply).
/// Initiator process: supplier requests DataHub to end supply and disconnect the metering point.
/// Creates a BRS process + outbox message for DataHub.
/// </summary>
public static class Brs002Handler
{
    public record EndOfSupplyResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    public record ConfirmationResult(
        BrsProcess Process,
        Supply EndedSupply);

    /// <summary>
    /// Step 1: Initiate end of supply — create process + outbox message.
    /// Supply is NOT ended yet — that happens when DataHub confirms disconnection.
    /// </summary>
    public static EndOfSupplyResult InitiateEndOfSupply(
        Gsrn gsrn,
        DateTimeOffset desiredEndDate,
        GlnNumber supplierGln,
        string reason)
    {
        var process = BrsProcess.Create(
            ProcessType.Supplyophør,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            desiredEndDate);

        var transactionId = $"BRS002-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Leveranceophør anmodning oprettet");
        process.MarkSubmitted(transactionId);

        // Create CIM JSON envelope for DataHub
        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm005, "E20", supplierGln.Value)
            .AddTransaction(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new CimDocumentBuilder.CimCodedValue("A10", gsrn.Value),
                ["end_DateAndOrTime.dateTime"] = desiredEndDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            })
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-005",
            senderGln: supplierGln.Value,
            receiverGln: "5790001330552", // DataHub GLN
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-002");

        return new EndOfSupplyResult(process, outbox);
    }

    /// <summary>
    /// Step 2: DataHub confirmed — end the supply.
    /// </summary>
    public static ConfirmationResult HandleConfirmation(
        BrsProcess process,
        Supply currentSupply,
        DateTimeOffset actualEndDate)
    {
        process.TransitionTo("Confirmed", "DataHub godkendt leveranceophør");
        process.MarkConfirmed();

        currentSupply.EndSupply(actualEndDate, process.Id);

        process.TransitionTo("Completed", "Leveranceophør gennemført — afventer slutsettlement");
        process.MarkCompleted();

        return new ConfirmationResult(process, currentSupply);
    }

    /// <summary>
    /// DataHub rejected the request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }
}
