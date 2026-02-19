using Microsoft.EntityFrameworkCore;
using WattsOn.Application.Interfaces;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;

namespace WattsOn.Infrastructure.Persistence;

public class WattsOnDbContext : DbContext, IWattsOnDbContext
{
    public WattsOnDbContext(DbContextOptions<WattsOnDbContext> options) : base(options) { }

    public DbSet<SupplierIdentity> SupplierIdentities => Set<SupplierIdentity>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MeteringPoint> MeteringPoints => Set<MeteringPoint>();
    public DbSet<Supply> Supplies => Set<Supply>();
    public DbSet<TimeSeries> TimeSeriesCollection => Set<TimeSeries>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<PricePoint> PricePoints => Set<PricePoint>();
    public DbSet<PriceLink> PriceLinks => Set<PriceLink>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SettlementLine> SettlementLines => Set<SettlementLine>();
    public DbSet<SpotPrice> SpotPrices => Set<SpotPrice>();
    public DbSet<BrsProcess> Processes => Set<BrsProcess>();
    public DbSet<ProcessStateTransition> ProcessTransitions => Set<ProcessStateTransition>();
    public DbSet<AggregatedTimeSeries> AggregatedTimeSeriesCollection => Set<AggregatedTimeSeries>();
    public DbSet<WholesaleSettlement> WholesaleSettlements => Set<WholesaleSettlement>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ReconciliationResult> ReconciliationResults => Set<ReconciliationResult>();
    public DbSet<ReconciliationLine> ReconciliationLines => Set<ReconciliationLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Exclude DomainEvent from EF model (it's an in-memory collection, not persisted)
        modelBuilder.Ignore<WattsOn.Domain.Common.DomainEvent>();

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WattsOnDbContext).Assembly);
    }
}
