using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// PriceLink — links a price/charge to a metering point for a period.
/// A metering point can have multiple charges (grid tariff, system tariff, etc.)
/// each active for different periods.
/// Received via BRS-031 (price link updates).
/// </summary>
public class PriceLink : Entity
{
    public Guid MeteringPointId { get; private set; }
    public Guid PriceId { get; private set; }

    /// <summary>Period during which this price applies to the metering point</summary>
    public Period LinkPeriod { get; private set; } = null!;

    // Navigation
    public MeteringPoint MeteringPoint { get; private set; } = null!;
    public Price Price { get; private set; } = null!;

    private PriceLink() { } // EF Core

    public static PriceLink Create(Guid meteringPointId, Guid priceId, Period linkPeriod)
    {
        return new PriceLink
        {
            MeteringPointId = meteringPointId,
            PriceId = priceId,
            LinkPeriod = linkPeriod
        };
    }

    public void EndLink(DateTimeOffset endDate)
    {
        LinkPeriod = LinkPeriod.ClosedAt(endDate);
        MarkUpdated();
    }
}
