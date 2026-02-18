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
    DbSet<Actor> Actors { get; }
    DbSet<Customer> Customers { get; }
    DbSet<MeteringPoint> MeteringPoints { get; }
    DbSet<Supply> Supplies { get; }
    DbSet<TimeSeries> TimeSeriesCollection { get; }
    DbSet<Observation> Observations { get; }
    DbSet<Price> Prices { get; }
    DbSet<PrisPoint> PrisPoints { get; }
    DbSet<PriceLink> PriceLinks { get; }
    DbSet<Settlement> Settlements { get; }
    DbSet<SettlementLinje> SettlementLinjer { get; }
    DbSet<BrsProcess> Processes { get; }
    DbSet<ProcessStateTransition> ProcessTransitions { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
