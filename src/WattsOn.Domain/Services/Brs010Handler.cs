using System.Text.Json;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-010 — Fraflytning (Move-Out).
/// Initiator process: supplier notifies DataHub that a customer is moving out.
/// Creates a BRS process + outbox message + ends the supply.
/// </summary>
public static class Brs010Handler
{
    public record MoveOutResult(
        BrsProcess Process,
        Supply EndedSupply,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Execute move-out: end supply and create outbox message to DataHub.
    /// Unlike BRS-002 (end of supply), move-out takes effect immediately.
    /// </summary>
    public static MoveOutResult ExecuteMoveOut(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        Supply currentSupply,
        GlnNumber supplierGln)
    {
        if (!currentSupply.IsActive)
            throw new InvalidOperationException("Supply is not active — cannot process move-out.");

        var process = BrsProcess.Create(
            ProcessType.Fraflytning,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            effectiveDate);

        var transactionId = $"BRS010-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Fraflytningsanmodning sendt til DataHub");
        process.MarkSubmitted(transactionId);
        process.TransitionTo("Confirmed", "DataHub godkendt");
        process.MarkConfirmed();

        // End the supply
        currentSupply.EndSupply(effectiveDate, process.Id);

        process.TransitionTo("Completed", "Fraflytning gennemført — afventer slutsettlement");
        process.MarkCompleted();

        // Create outbox message to DataHub
        var payload = JsonSerializer.Serialize(new
        {
            businessReason = "E66",
            gsrn = gsrn.Value,
            endDate = effectiveDate
        });

        var outbox = OutboxMessage.Create(
            documentType: "RSM-005",
            senderGln: supplierGln.Value,
            receiverGln: "5790001330552", // DataHub GLN
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-010");

        return new MoveOutResult(process, currentSupply, outbox);
    }
}
