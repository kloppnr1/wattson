using System.Text.Json;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes;
using WattsOn.Domain.Processes.StateMachines;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-001 (Leverandørskift) process steps.
/// Supports both initiator (gaining customer) and recipient (losing customer) roles.
/// Pure domain logic — no persistence, returns commands for the caller to execute.
/// </summary>
public static class Brs001Handler
{
    // --- Data structures for the process ---

    public record SupplierChangeRequest(
        string Gsrn,
        DateTimeOffset EffectiveDate,
        string? CprNumber,
        string? CvrNumber,
        string CustomerName,
        string? Email,
        string? Phone,
        AddressData? Address);

    public record AddressData(
        string StreetName, string BuildingNumber, string PostCode, string CityName,
        string? Floor = null, string? Suite = null);

    public record SupplierChangeResult(
        BrsProcess Process,
        Leverance? NewLeverance,
        Leverance? EndedLeverance);

    /// <summary>
    /// Step 1 (Initiator): We request a supplier change — gaining this customer.
    /// Creates the BRS process and validates basic requirements.
    /// </summary>
    public static BrsProcess InitiateSupplierChange(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        string? cprNumber,
        string? cvrNumber,
        GlnNumber counterpartGln)
    {
        if (cprNumber is null && cvrNumber is null)
            throw new InvalidOperationException("Either CPR or CVR is required for supplier change.");

        // In production, effective date must be in the future.
        // For simulation/testing, we allow past dates.

        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        var process = BrsProcess.Create(
            ProcessType.Leverandørskift,
            ProcessRole.Initiator,
            sm.InitialState,
            gsrn,
            effectiveDate,
            counterpartGln);

        return process;
    }

    /// <summary>
    /// Step 2 (Initiator): DataHub confirmed our request.
    /// </summary>
    public static void HandleConfirmation(BrsProcess process, string transactionId)
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        if (!sm.CanTransition(process.CurrentState, Brs001StateMachine.Submitted))
        {
            // If still in Created, mark as submitted first
            if (process.CurrentState == Brs001StateMachine.Created)
                process.TransitionTo(Brs001StateMachine.Submitted, "Request submitted to DataHub");
        }

        process.MarkSubmitted(transactionId);
        process.TransitionTo(Brs001StateMachine.Confirmed, "DataHub confirmed supplier change");
        process.MarkConfirmed();
    }

    /// <summary>
    /// Step 3 (Initiator): DataHub rejected our request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo(Brs001StateMachine.Rejected, reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Step 4 (Initiator): Master data received — the change is effective.
    /// Creates the new leverance and ends the old one.
    /// </summary>
    public static SupplierChangeResult ExecuteSupplierChange(
        BrsProcess process,
        Målepunkt målepunkt,
        Kunde kunde,
        Guid ownAktørId,
        Leverance? currentLeverance)
    {
        process.TransitionTo(Brs001StateMachine.Active, "Master data received, executing change");

        // End current leverance if exists
        if (currentLeverance is not null)
        {
            currentLeverance.EndSupply(process.EffectiveDate!.Value, process.Id);
        }

        // Create new leverance
        var supplyPeriod = Period.From(process.EffectiveDate!.Value);
        var newLeverance = Leverance.Create(
            målepunkt.Id,
            kunde.Id,
            ownAktørId,
            supplyPeriod);

        process.TransitionTo(Brs001StateMachine.Completed, "Supplier change completed");
        process.MarkCompleted();

        return new SupplierChangeResult(process, newLeverance, currentLeverance);
    }

    /// <summary>
    /// Recipient flow: We're losing a customer — DataHub notifies us of stop-of-supply.
    /// Creates the process and ends our leverance.
    /// </summary>
    public static SupplierChangeResult HandleAsRecipient(
        Gsrn gsrn,
        DateTimeOffset effectiveDate,
        string transactionId,
        GlnNumber newSupplierGln,
        Leverance currentLeverance)
    {
        var sm = new Brs001StateMachine(ProcessRole.Recipient);
        var process = BrsProcess.Create(
            ProcessType.Leverandørskift,
            ProcessRole.Recipient,
            sm.InitialState,
            gsrn,
            effectiveDate,
            newSupplierGln,
            transactionId);

        process.TransitionTo(Brs001StateMachine.Acknowledged, "Stop-of-supply notification received");
        process.TransitionTo(Brs001StateMachine.AwaitingEffectiveDate, "Awaiting effective date");

        // End our leverance
        currentLeverance.EndSupply(effectiveDate, process.Id);

        process.TransitionTo(Brs001StateMachine.FinalSettlement, "Leverance ended, awaiting final settlement");
        process.TransitionTo(Brs001StateMachine.Completed, "Final settlement period closed");
        process.MarkCompleted();

        return new SupplierChangeResult(process, null, currentLeverance);
    }
}
