using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-041 — Elvarme (Electrical Heating).
/// Initiator-only: add or remove electrical heating status on a consumption MP.
/// This affects tax tariffs for the metering point.
///
/// Request: RSM-027/D20 → DataHub (DDZ role)
/// Response: Accept/reject from DataHub
///
/// Reuses RSM-027 (same as BRS-015) but with processType D20 instead of E34.
/// </summary>
public static class Brs041Handler
{
    public record ElectricalHeatingResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request to add or remove electrical heating on a metering point.
    /// </summary>
    /// <param name="gsrn">Metering point GSRN</param>
    /// <param name="ourGln">Our GLN (sender)</param>
    /// <param name="action">"Add" or "Remove"</param>
    /// <param name="effectiveDate">Effective date of the change</param>
    public static ElectricalHeatingResult RequestElectricalHeatingChange(
        Gsrn gsrn,
        GlnNumber ourGln,
        string action,
        DateTimeOffset effectiveDate)
    {
        if (!action.Equals("Add", StringComparison.OrdinalIgnoreCase) &&
            !action.Equals("Remove", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Invalid action '{action}'. Valid values: Add, Remove.",
                nameof(action));
        }

        var process = BrsProcess.Create(
            ProcessType.Elvarme,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            effectiveDate);

        var transactionId = $"BRS041-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        var actionDesc = action.Equals("Add", StringComparison.OrdinalIgnoreCase)
            ? "tilføjelse" : "fjernelse";
        process.TransitionTo("Submitted", $"Elvarme {actionDesc} sendt til DataHub");
        process.MarkSubmitted(transactionId);

        var seriesFields = new Dictionary<string, object?>
        {
            ["marketEvaluationPoint.mRID"] = new { codingScheme = "A10", value = gsrn.Value },
            ["start_DateAndOrTime.dateTime"] = effectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["electricalHeating"] = action.ToLowerInvariant(),
        };

        // Reuse RSM-027 (same as BRS-015) but with processType D20
        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm027, "D20", ourGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-027",
            senderGln: ourGln.Value,
            receiverGln: CimDocumentBuilder.DataHubGln,
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-041");

        return new ElectricalHeatingResult(process, outbox);
    }

    /// <summary>
    /// DataHub accepted the electrical heating change.
    /// </summary>
    public static void HandleAcceptance(BrsProcess process)
    {
        process.TransitionTo("Accepted", "Elvarme ændring godkendt af DataHub");
        process.MarkConfirmed();
        process.TransitionTo("Completed", "Elvarme ændring gennemført");
        process.MarkCompleted();
    }

    /// <summary>
    /// DataHub rejected the electrical heating change.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }
}
