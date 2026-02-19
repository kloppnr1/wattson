using System.Text.Json;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-003 — Fejlagtigt leverandørskift (Incorrect Supplier Switch).
/// Bidirectional: we can initiate a reversal (we're the current supplier reporting the error)
/// or receive a reversal request (we're the previous supplier being asked to resume supply).
///
/// Initiator flow: Report error → DataHub asks previous supplier → accept/reject → supply corrected.
/// Recipient flow: DataHub asks us to resume supply → we accept or reject.
/// </summary>
public static class Brs003Handler
{
    // ==================== INITIATOR (we report the erroneous switch) ====================

    public record InitiateReversalResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Initiate correction of an erroneous supplier switch.
    /// Creates a BRS-003 process and outbox message to DataHub.
    /// The switch date must be within 60 calendar days.
    /// </summary>
    public static InitiateReversalResult InitiateReversal(
        Gsrn gsrn,
        DateTimeOffset switchDate,
        GlnNumber ourGln,
        string reason)
    {
        var daysSinceSwitch = (DateTimeOffset.UtcNow - switchDate).TotalDays;
        if (daysSinceSwitch > 60)
            throw new InvalidOperationException(
                $"Cannot correct supplier switch — switch date is {daysSinceSwitch:F0} days ago (max 60).");

        var process = BrsProcess.Create(
            ProcessType.FejlagtigtLeverandørskift,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            switchDate);

        var transactionId = $"BRS003-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Anmodning om korrektion af fejlagtigt leverandørskift sendt");
        process.MarkSubmitted(transactionId);

        var payload = JsonSerializer.Serialize(new
        {
            businessReason = "D07",
            gsrn = gsrn.Value,
            switchDate,
            reason
        });

        var outbox = OutboxMessage.Create(
            documentType: "RSM-001",
            senderGln: ourGln.Value,
            receiverGln: "5790001330552",
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-003");

        return new InitiateReversalResult(process, outbox);
    }

    /// <summary>
    /// DataHub confirmed the correction (RSM-004/D34) — the previous supplier accepted.
    /// We need to end our supply on this metering point.
    /// </summary>
    public static Supply? HandleCorrectionAccepted(
        BrsProcess process,
        Supply? ourSupply,
        DateTimeOffset correctionDate)
    {
        process.TransitionTo("Accepted", "Hidtidig leverandør har accepteret genoptagelse");
        process.MarkConfirmed();

        if (ourSupply is not null)
        {
            ourSupply.EndSupply(correctionDate, process.Id);
        }

        process.TransitionTo("Completed", "Leverandørskift korrigeret — leverance afsluttet");
        process.MarkCompleted();

        return ourSupply;
    }

    /// <summary>
    /// DataHub informed us the correction was rejected (RSM-004/D35).
    /// </summary>
    public static void HandleCorrectionRejected(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    // ==================== RECIPIENT (we're asked to resume supply) ====================

    public record ResumeSupplyResult(
        BrsProcess Process,
        Supply NewSupply);

    /// <summary>
    /// Receive a request to resume supply (RSM-003/D07).
    /// Creates a BRS-003 process in Received state.
    /// In production, the supplier would review and accept/reject via the market portal.
    /// For WattsOn, we auto-accept and recreate the supply.
    /// </summary>
    public static ResumeSupplyResult HandleResumeRequest(
        Gsrn gsrn,
        DateTimeOffset resumeDate,
        string transactionId,
        GlnNumber currentSupplierGln,
        MeteringPoint meteringPoint,
        Customer customer)
    {
        var process = BrsProcess.Create(
            ProcessType.FejlagtigtLeverandørskift,
            ProcessRole.Recipient,
            "Received",
            gsrn,
            resumeDate,
            currentSupplierGln,
            transactionId);

        process.TransitionTo("Accepted", "Genoptagelse af leverance accepteret");
        process.MarkConfirmed();

        // Create new supply from the resume date
        var supplyPeriod = Period.From(resumeDate);
        var newSupply = Supply.Create(meteringPoint.Id, customer.Id, supplyPeriod);

        process.TransitionTo("Completed", "Leverance genoptaget");
        process.MarkCompleted();

        return new ResumeSupplyResult(process, newSupply);
    }
}
