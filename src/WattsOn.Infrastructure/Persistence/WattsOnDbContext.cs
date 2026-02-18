using Microsoft.EntityFrameworkCore;
using WattsOn.Application.Interfaces;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;

namespace WattsOn.Infrastructure.Persistence;

public class WattsOnDbContext : DbContext, IWattsOnDbContext
{
    public WattsOnDbContext(DbContextOptions<WattsOnDbContext> options) : base(options) { }

    public DbSet<Actor> Actors => Set<Actor>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<MeteringPoint> MeteringPoints => Set<MeteringPoint>();
    public DbSet<Supply> Supplies => Set<Supply>();
    public DbSet<TimeSeries> TimeSeriesCollection => Set<TimeSeries>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Price> Prices => Set<Price>();
    public DbSet<PrisPoint> PrisPoints => Set<PrisPoint>();
    public DbSet<PriceLink> PriceLinks => Set<PriceLink>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SettlementLinje> SettlementLinjer => Set<SettlementLinje>();
    public DbSet<BrsProcess> Processes => Set<BrsProcess>();
    public DbSet<ProcessStateTransition> ProcessTransitions => Set<ProcessStateTransition>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WattsOnDbContext).Assembly);
    }
}
