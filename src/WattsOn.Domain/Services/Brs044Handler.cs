using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-044 — Tvunget leverandørskift (Mandatory Supplier Switch / Forced Transfer).
/// DataHub forces supplier switch due to bankruptcy, merger, or GLN transfer.
/// Two scenarios:
///   a) Incoming: We're the new supplier — create/update MPs, create supplies.
///   b) Outgoing: We're the former supplier — end active supplies.
/// Pure domain logic — no persistence.
/// </summary>
public static class Brs044Handler
{
    public record IncomingTransferData(
        Gsrn Gsrn,
        DateTimeOffset EffectiveDate,
        Guid CustomerId,
        Guid MeteringPointId);

    public record OutgoingTransferData(
        Gsrn Gsrn,
        DateTimeOffset EffectiveDate);

    public record TransferResult(
        BrsProcess Process,
        Supply? NewSupply = null,
        Supply? EndedSupply = null);

    /// <summary>
    /// Handle an incoming transfer — we're becoming the new supplier.
    /// Creates a new supply for the transferred metering point.
    /// </summary>
    public static TransferResult HandleIncomingTransfer(IncomingTransferData data)
    {
        var process = BrsProcess.Create(
            ProcessType.TvungetLeverandørskift,
            ProcessRole.Recipient,
            "Received",
            data.Gsrn,
            data.EffectiveDate);

        var supplyPeriod = Period.From(data.EffectiveDate);
        var supply = Supply.Create(
            data.MeteringPointId,
            data.CustomerId,
            supplyPeriod,
            process.Id);

        process.TransitionTo("SupplyCreated", "Incoming transfer: new supply created");
        process.TransitionTo("Completed", "Incoming mandatory transfer completed");
        process.MarkCompleted();

        return new TransferResult(process, NewSupply: supply);
    }

    /// <summary>
    /// Handle an outgoing transfer — we're losing the metering point.
    /// Ends the active supply at the effective date.
    /// </summary>
    public static TransferResult HandleOutgoingTransfer(
        OutgoingTransferData data,
        MeteringPoint mp,
        Supply? activeSupply)
    {
        var process = BrsProcess.Create(
            ProcessType.TvungetLeverandørskift,
            ProcessRole.Recipient,
            "Received",
            data.Gsrn,
            data.EffectiveDate);

        if (activeSupply is not null)
        {
            activeSupply.EndSupply(data.EffectiveDate, process.Id);
            mp.SetActiveSupply(false);
            process.TransitionTo("SupplyEnded", "Outgoing transfer: active supply ended");
        }
        else
        {
            process.TransitionTo("NoActiveSupply", "Outgoing transfer: no active supply to end");
        }

        process.TransitionTo("Completed", "Outgoing mandatory transfer completed");
        process.MarkCompleted();

        return new TransferResult(process, EndedSupply: activeSupply);
    }
}
