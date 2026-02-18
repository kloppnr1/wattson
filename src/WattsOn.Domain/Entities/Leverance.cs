using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Leverance — a supply agreement linking our company to a metering point for a customer.
/// Represents the period during which we supply electricity to a specific metering point.
/// Created via BRS-001 (supplier change), BRS-009 (move-in).
/// Ended via BRS-002 (supply cessation), BRS-010 (move-out), or another BRS-001.
/// </summary>
public class Leverance : Entity
{
    public Guid MålepunktId { get; private set; }
    public Guid KundeId { get; private set; }
    public Guid AktørId { get; private set; }

    /// <summary>The period during which this supply is active [start, end)</summary>
    public Period SupplyPeriod { get; private set; } = null!;

    /// <summary>Whether this supply agreement is currently active</summary>
    public bool IsActive => SupplyPeriod.IsActive();

    /// <summary>Reference to the BRS process that created this supply</summary>
    public Guid? CreatedByProcessId { get; private set; }

    /// <summary>Reference to the BRS process that ended this supply</summary>
    public Guid? EndedByProcessId { get; private set; }

    // Navigation properties
    public Målepunkt Målepunkt { get; private set; } = null!;
    public Kunde Kunde { get; private set; } = null!;
    public Aktør Aktør { get; private set; } = null!;

    private Leverance() { } // EF Core

    public static Leverance Create(
        Guid målepunktId,
        Guid kundeId,
        Guid aktørId,
        Period supplyPeriod,
        Guid? createdByProcessId = null)
    {
        return new Leverance
        {
            MålepunktId = målepunktId,
            KundeId = kundeId,
            AktørId = aktørId,
            SupplyPeriod = supplyPeriod,
            CreatedByProcessId = createdByProcessId
        };
    }

    /// <summary>
    /// End this supply agreement at a specific date.
    /// Called when we lose a customer (BRS-001/002/010).
    /// </summary>
    public void EndSupply(DateTimeOffset endDate, Guid? endedByProcessId = null)
    {
        if (!SupplyPeriod.IsOpenEnded && SupplyPeriod.End <= endDate)
            throw new InvalidOperationException("Supply already ended before the specified date.");

        SupplyPeriod = SupplyPeriod.ClosedAt(endDate);
        EndedByProcessId = endedByProcessId;
        MarkUpdated();
    }
}
