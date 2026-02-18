using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Customer — an end customer.
/// Can be a person (CPR) or a company (CVR).
/// Linked to metering points via Supply (supply agreement).
/// </summary>
public class Customer : Entity
{
    public string Name { get; private set; } = null!;
    public CprNumber? Cpr { get; private set; }
    public CvrNumber? Cvr { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public Address? Address { get; private set; }

    /// <summary>The supplier identity (GLN) that owns this customer</summary>
    public Guid SupplierIdentityId { get; private set; }

    // Navigation
    public SupplierIdentity SupplierIdentity { get; private set; } = null!;

    /// <summary>Customer's active supply agreements</summary>
    private readonly List<Supply> _supplies = new();
    public IReadOnlyList<Supply> Supplies => _supplies.AsReadOnly();

    private Customer() { } // EF Core

    public static Customer CreatePerson(string name, CprNumber cpr, Guid supplierIdentityId, Address? address = null)
    {
        return new Customer
        {
            Name = name,
            Cpr = cpr,
            SupplierIdentityId = supplierIdentityId,
            Address = address
        };
    }

    public static Customer CreateCompany(string name, CvrNumber cvr, Guid supplierIdentityId, Address? address = null)
    {
        return new Customer
        {
            Name = name,
            Cvr = cvr,
            SupplierIdentityId = supplierIdentityId,
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
