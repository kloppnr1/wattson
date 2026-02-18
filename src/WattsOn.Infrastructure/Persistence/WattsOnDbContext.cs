using Microsoft.EntityFrameworkCore;
using WattsOn.Application.Interfaces;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;

namespace WattsOn.Infrastructure.Persistence;

public class WattsOnDbContext : DbContext, IWattsOnDbContext
{
    public WattsOnDbContext(DbContextOptions<WattsOnDbContext> options) : base(options) { }

    public DbSet<Aktør> Aktører => Set<Aktør>();
    public DbSet<Kunde> Kunder => Set<Kunde>();
    public DbSet<Målepunkt> Målepunkter => Set<Målepunkt>();
    public DbSet<Leverance> Leverancer => Set<Leverance>();
    public DbSet<Tidsserie> Tidsserier => Set<Tidsserie>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<Pris> Priser => Set<Pris>();
    public DbSet<PrisPoint> PrisPoints => Set<PrisPoint>();
    public DbSet<Pristilknytning> Pristilknytninger => Set<Pristilknytning>();
    public DbSet<Afregning> Afregninger => Set<Afregning>();
    public DbSet<AfregningLinje> AfregningLinjer => Set<AfregningLinje>();
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
