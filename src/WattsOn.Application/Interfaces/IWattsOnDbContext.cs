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
    DbSet<Aktør> Aktører { get; }
    DbSet<Kunde> Kunder { get; }
    DbSet<Målepunkt> Målepunkter { get; }
    DbSet<Leverance> Leverancer { get; }
    DbSet<Tidsserie> Tidsserier { get; }
    DbSet<Observation> Observations { get; }
    DbSet<Pris> Priser { get; }
    DbSet<PrisPoint> PrisPoints { get; }
    DbSet<Pristilknytning> Pristilknytninger { get; }
    DbSet<Afregning> Afregninger { get; }
    DbSet<AfregningLinje> AfregningLinjer { get; }
    DbSet<BrsProcess> Processes { get; }
    DbSet<ProcessStateTransition> ProcessTransitions { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
