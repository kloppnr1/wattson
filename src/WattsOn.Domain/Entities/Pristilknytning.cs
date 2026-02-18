using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Pristilknytning — links a price/charge to a metering point for a period.
/// A metering point can have multiple charges (grid tariff, system tariff, etc.)
/// each active for different periods.
/// Received via BRS-031 (price link updates).
/// </summary>
public class Pristilknytning : Entity
{
    public Guid MålepunktId { get; private set; }
    public Guid PrisId { get; private set; }

    /// <summary>Period during which this price applies to the metering point</summary>
    public Period LinkPeriod { get; private set; } = null!;

    // Navigation
    public Målepunkt Målepunkt { get; private set; } = null!;
    public Pris Pris { get; private set; } = null!;

    private Pristilknytning() { } // EF Core

    public static Pristilknytning Create(Guid målepunktId, Guid prisId, Period linkPeriod)
    {
        return new Pristilknytning
        {
            MålepunktId = målepunktId,
            PrisId = prisId,
            LinkPeriod = linkPeriod
        };
    }

    public void EndLink(DateTimeOffset endDate)
    {
        LinkPeriod = LinkPeriod.ClosedAt(endDate);
        MarkUpdated();
    }
}
