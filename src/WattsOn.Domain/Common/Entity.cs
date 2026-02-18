using System.ComponentModel.DataAnnotations.Schema;

namespace WattsOn.Domain.Common;

/// <summary>
/// Base class for all domain entities.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; protected set; }

    private readonly List<DomainEvent> _domainEvents = new();

    [NotMapped]
    public IReadOnlyList<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(DomainEvent domainEvent)
    {
        _domainEvents.Add(domainEvent);
    }

    public void ClearDomainEvents()
    {
        _domainEvents.Clear();
    }

    protected void MarkUpdated()
    {
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other) return false;
        if (ReferenceEquals(this, other)) return true;
        if (GetType() != other.GetType()) return false;
        return Id == other.Id;
    }

    public override int GetHashCode() => Id.GetHashCode();
}
