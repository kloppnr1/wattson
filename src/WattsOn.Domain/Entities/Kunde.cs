using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Kunde — an end customer.
/// Can be a person (CPR) or a company (CVR).
/// Linked to metering points via Leverance (supply agreement).
/// </summary>
public class Kunde : Entity
{
    public string Name { get; private set; } = null!;
    public CprNumber? Cpr { get; private set; }
    public CvrNumber? Cvr { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public Address? Address { get; private set; }

    /// <summary>Customer's active supply agreements</summary>
    private readonly List<Leverance> _leverancer = new();
    public IReadOnlyList<Leverance> Leverancer => _leverancer.AsReadOnly();

    private Kunde() { } // EF Core

    public static Kunde CreatePerson(string name, CprNumber cpr, Address? address = null)
    {
        return new Kunde
        {
            Name = name,
            Cpr = cpr,
            Address = address
        };
    }

    public static Kunde CreateCompany(string name, CvrNumber cvr, Address? address = null)
    {
        return new Kunde
        {
            Name = name,
            Cvr = cvr,
            Address = address
        };
    }

    public void UpdateContactInfo(string? email = null, string? phone = null)
    {
        if (email is not null) Email = email;
        if (phone is not null) Phone = phone;
        MarkUpdated();
    }

    public void UpdateAddress(Address address)
    {
        Address = address;
        MarkUpdated();
    }

    public void UpdateName(string name)
    {
        Name = name;
        MarkUpdated();
    }

    /// <summary>Is this a private person (has CPR)?</summary>
    public bool IsPrivate => Cpr is not null;

    /// <summary>Is this a company (has CVR)?</summary>
    public bool IsCompany => Cvr is not null;
}
