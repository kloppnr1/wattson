using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;

namespace WattsOn.Application.Interfaces;

/// <summary>
/// Application-level database context interface.
/// Infrastructure provides the implementation.
/// </summary>
public interface IWattsOnDbContext
{
    DbSet<SupplierIdentity> SupplierIdentities { get; }
    DbSet<Customer> Customers { get; }
    DbSet<MeteringPoint> MeteringPoints { get; }
    DbSet<Supply> Supplies { get; }
    DbSet<TimeSeries> TimeSeriesCollection { get; }
    DbSet<Observation> Observations { get; }
    DbSet<Price> Prices { get; }
    DbSet<PricePoint> PricePoints { get; }
    DbSet<PriceLink> PriceLinks { get; }
    DbSet<Settlement> Settlements { get; }
    DbSet<SettlementLine> SettlementLines { get; }
    DbSet<SpotPrice> SpotPrices { get; }
    DbSet<BrsProcess> Processes { get; }
    DbSet<ProcessStateTransition> ProcessTransitions { get; }
    DbSet<AggregatedTimeSeries> AggregatedTimeSeriesCollection { get; }
    DbSet<WholesaleSettlement> WholesaleSettlements { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<ReconciliationResult> ReconciliationResults { get; }
    DbSet<ReconciliationLine> ReconciliationLines { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
