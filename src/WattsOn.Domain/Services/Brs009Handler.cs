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
        Leverance NewLeverance,
        Leverance? EndedLeverance);

    public record MoveOutResult(
        BrsProcess Process,
        Leverance EndedLeverance);

    /// <summary>
    /// Move-in (initiator): Register a new customer at a metering point.
    /// Creates the BRS process and new leverance.
    /// </summary>
    public static MoveInResult ExecuteMoveIn(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        string? cprNumber,
        string? cvrNumber,
        Målepunkt målepunkt,
        Kunde kunde,
        Guid ownAktørId,
        Leverance? currentLeverance)
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

        // End current leverance if different customer
        if (currentLeverance is not null)
        {
            currentLeverance.EndSupply(effectiveDate, process.Id);
        }

        // Create new leverance
        var newLeverance = Leverance.Create(
            målepunkt.Id, kunde.Id, ownAktørId,
            Period.From(effectiveDate), process.Id);

        process.TransitionTo(Brs009StateMachine.Completed, "Tilflytning gennemført");
        process.MarkCompleted();

        return new MoveInResult(process, newLeverance, currentLeverance);
    }

    /// <summary>
    /// Move-out: Customer leaves. End the supply at the effective date.
    /// This triggers final settlement for the remaining period.
    /// </summary>
    public static MoveOutResult ExecuteMoveOut(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        Leverance currentLeverance)
    {
        if (!currentLeverance.IsActive)
            throw new InvalidOperationException("Leverance is not active — cannot process move-out.");

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
        currentLeverance.EndSupply(effectiveDate, process.Id);

        process.TransitionTo("Completed", "Fraflytning gennemført — afventer slutafregning");
        process.MarkCompleted();

        return new MoveOutResult(process, currentLeverance);
    }
}
