namespace WattsOn.Domain.Common;

/// <summary>
/// Base class for domain events.
/// </summary>
public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
