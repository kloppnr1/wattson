using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-011 — Fejlagtig flytning (Incorrect Move).
/// Bidirectional: we can report our own erroneous move-in/move-out,
/// or receive a request to resume supply when another supplier's move was erroneous.
///
/// Only "simple" cases have EDI messages — complex cases go to DataHub Support.
/// We handle the simple case.
/// </summary>
public static class Brs011Handler
{
    // ==================== INITIATOR (we report our erroneous move) ====================

    public record InitiateReversalResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Report an erroneous move-in or move-out.
    /// The move date must be within 60 calendar days.
    /// Creates a BRS-011 process and outbox message.
    /// </summary>
    public static InitiateReversalResult InitiateReversal(
        Gsrn gsrn,
        DateTimeOffset moveDate,
        GlnNumber ourGln,
        string moveType,
        string reason)
    {
        if (moveType != "move-in" && moveType != "move-out")
            throw new ArgumentException("moveType must be 'move-in' or 'move-out'", nameof(moveType));

        var daysSinceMove = (DateTimeOffset.UtcNow - moveDate).TotalDays;
        if (daysSinceMove > 60)
            throw new InvalidOperationException(
                $"Cannot correct move — move date is {daysSinceMove:F0} days ago (max 60).");

        var process = BrsProcess.Create(
            ProcessType.FejlagtigFlytning,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            moveDate);

        var transactionId = $"BRS011-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", $"Anmodning om korrektion af fejlagtig {moveType} sendt");
        process.MarkSubmitted(transactionId);

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm005, "D33", ourGln.Value)
            .AddSeries(new Dictionary<string, object?>
            {
                ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
                ["start_DateAndOrTime.dateTime"] = moveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                ["moveType"] = moveType,
                ["reason"] = reason,
            })
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-005",
            senderGln: ourGln.Value,
            receiverGln: "5790001330552",
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-011");

        return new InitiateReversalResult(process, outbox);
    }

    /// <summary>
    /// DataHub confirmed the correction (RSM-004/D34).
    /// End our supply if we were the erroneous move-in supplier.
    /// </summary>
    public static Supply? HandleCorrectionAccepted(
        BrsProcess process,
        Supply? ourSupply,
        DateTimeOffset correctionDate)
    {
        process.TransitionTo("Accepted", "Fejlagtig flytning accepteret af hidtidig leverandør");
        process.MarkConfirmed();

        if (ourSupply is not null)
        {
            ourSupply.EndSupply(correctionDate, process.Id);
        }

        process.TransitionTo("Completed", "Flytning korrigeret");
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
    /// Receive a request to resume supply (RSM-003/D33).
    /// Auto-accept and recreate the supply.
    /// </summary>
    public static ResumeSupplyResult HandleResumeRequest(
        Gsrn gsrn,
        DateTimeOffset resumeDate,
        string transactionId,
        GlnNumber erroneousSupplierGln,
        MeteringPoint meteringPoint,
        Customer customer)
    {
        var process = BrsProcess.Create(
            ProcessType.FejlagtigFlytning,
            ProcessRole.Recipient,
            "Received",
            gsrn,
            resumeDate,
            erroneousSupplierGln,
            transactionId);

        process.TransitionTo("Accepted", "Genoptagelse af leverance accepteret");
        process.MarkConfirmed();

        var supplyPeriod = Period.From(resumeDate);
        var newSupply = Supply.Create(meteringPoint.Id, customer.Id, supplyPeriod);

        process.TransitionTo("Completed", "Leverance genoptaget efter fejlagtig flytning");
        process.MarkCompleted();

        return new ResumeSupplyResult(process, newSupply);
    }
}
