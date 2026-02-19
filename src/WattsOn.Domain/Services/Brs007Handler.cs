using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-007 — Nedlæggelse af målepunkt (Closedown of Metering Point).
/// Grid company permanently closes a metering point. DataHub notifies supplier.
/// We end any active supply and mark the MP as decommissioned (Nedlagt).
/// Pure domain logic — no persistence.
/// </summary>
public static class Brs007Handler
{
    public record DecommissionData(
        Gsrn Gsrn,
        DateTimeOffset EffectiveDate,
        string? Reason = null);

    public record DecommissionResult(
        MeteringPoint MeteringPoint,
        BrsProcess Process,
        Supply? EndedSupply,
        ConnectionState PreviousState);

    /// <summary>
    /// Decommission a metering point: end active supply and set state to Nedlagt.
    /// </summary>
    public static DecommissionResult Decommission(
        MeteringPoint mp,
        DecommissionData data,
        Supply? activeSupply = null)
    {
        var previousState = mp.ConnectionState;

        // Create audit process
        var process = BrsProcess.Create(
            ProcessType.MålepunktNedlæggelse,
            ProcessRole.Recipient,
            "Received",
            data.Gsrn,
            data.EffectiveDate);

        // End active supply if exists
        if (activeSupply is not null)
        {
            activeSupply.EndSupply(data.EffectiveDate, process.Id);
            mp.SetActiveSupply(false);
            process.TransitionTo("SupplyEnded", "Active supply ended");
        }

        // Mark metering point as decommissioned
        mp.UpdateConnectionState(ConnectionState.Nedlagt);

        var reason = data.Reason ?? "Metering point decommissioned (BRS-007)";
        process.TransitionTo("Completed", reason);
        process.MarkCompleted();

        return new DecommissionResult(mp, process, activeSupply, previousState);
    }
}
