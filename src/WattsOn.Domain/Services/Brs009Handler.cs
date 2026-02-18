using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes;
using WattsOn.Domain.Processes.StateMachines;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-009 (Tilflytning / Move-In) and the reverse move-out flow.
/// Move-in: New customer at a metering point. We register the supply.
/// Move-out: Our customer leaves. We end the supply and do final settlement.
/// </summary>
public static class Brs009Handler
{
    public record MoveInResult(
        BrsProcess Process,
        Supply NewSupply,
        Supply? EndedSupply);

    public record MoveOutResult(
        BrsProcess Process,
        Supply EndedSupply);

    /// <summary>
    /// Move-in (initiator): Register a new customer at a metering point.
    /// Creates the BRS process and new supply.
    /// </summary>
    public static MoveInResult ExecuteMoveIn(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        string? cprNumber,
        string? cvrNumber,
        MeteringPoint metering_point,
        Customer customer,
        Supply? currentSupply)
    {
        if (cprNumber is null && cvrNumber is null)
            throw new InvalidOperationException("Either CPR or CVR is required for move-in.");

        var sm = new Brs009StateMachine();
        var process = BrsProcess.Create(
            ProcessType.Tilflytning,
            ProcessRole.Initiator,
            sm.InitialState,
            gsrn,
            effectiveDate);

        // Simulate DataHub flow: submit → confirm → execute
        var transactionId = $"DH-SIM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo(Brs009StateMachine.Submitted, "Tilflytningsanmodning sendt til DataHub");
        process.MarkSubmitted(transactionId);
        process.TransitionTo(Brs009StateMachine.Confirmed, "DataHub godkendt");
        process.MarkConfirmed();

        // End current supply if different customer
        if (currentSupply is not null)
        {
            currentSupply.EndSupply(effectiveDate, process.Id);
        }

        // Create new supply
        var newSupply = Supply.Create(
            metering_point.Id, customer.Id,
            Period.From(effectiveDate), process.Id);

        process.TransitionTo(Brs009StateMachine.Completed, "Tilflytning gennemført");
        process.MarkCompleted();

        return new MoveInResult(process, newSupply, currentSupply);
    }

    /// <summary>
    /// Move-out: Customer leaves. End the supply at the effective date.
    /// This triggers final settlement for the remaining period.
    /// </summary>
    public static MoveOutResult ExecuteMoveOut(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        Supply currentSupply)
    {
        if (!currentSupply.IsActive)
            throw new InvalidOperationException("Supply is not active — cannot process move-out.");

        // Move-out uses Fraflytning process type
        var process = BrsProcess.Create(
            ProcessType.Fraflytning,
            ProcessRole.Initiator,
            "Created",
            gsrn,
            effectiveDate);

        var transactionId = $"DH-SIM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Fraflytningsanmodning sendt til DataHub");
        process.MarkSubmitted(transactionId);
        process.TransitionTo("Confirmed", "DataHub godkendt");
        process.MarkConfirmed();

        // End the supply
        currentSupply.EndSupply(effectiveDate, process.Id);

        process.TransitionTo("Completed", "Fraflytning gennemført — afventer slutsettlement");
        process.MarkCompleted();

        return new MoveOutResult(process, currentSupply);
    }
}
