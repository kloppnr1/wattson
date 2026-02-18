using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Actor — a market participant in the Danish electricity market.
/// Could be us (elleverandør), a grid company, DataHub, etc.
/// </summary>
public class Actor : Entity
{
    public GlnNumber Gln { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public ActorRole Role { get; private set; }
    public CvrNumber? Cvr { get; private set; }
    public bool IsOwn { get; private set; }

    private Actor() { } // EF Core

    public static Actor Create(GlnNumber gln, string name, ActorRole role, CvrNumber? cvr = null, bool isOwn = false)
    {
        return new Actor
        {
            Gln = gln,
            Name = name,
            Role = role,
            Cvr = cvr,
            IsOwn = isOwn
        };
    }

    /// <summary>Create our own company actor (elleverandør)</summary>
    public static Actor CreateOwn(GlnNumber gln, string name, CvrNumber cvr)
    {
        return Create(gln, name, ActorRole.Elleverandør, cvr, isOwn: true);
    }

    public void UpdateName(string name)
    {
        Name = name;
        MarkUpdated();
    }
}
