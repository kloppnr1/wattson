using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// A GLN identity that WattsOn operates as.
/// Each represents an elleverandør (electricity supplier) — either our primary company
/// or a legacy identity acquired through mergers that still receives corrected time series.
/// </summary>
public class SupplierIdentity : Entity
{
    public GlnNumber Gln { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public CvrNumber? Cvr { get; private set; }

    /// <summary>
    /// Active = currently trading, taking new customers.
    /// Legacy = acquired/merged, only processing corrections for historical periods.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Archived = fully retired, no longer receives any corrections.
    /// Hidden from all operational views.
    /// </summary>
    public bool IsArchived { get; private set; }

    private SupplierIdentity() { } // EF Core

    public static SupplierIdentity Create(GlnNumber gln, string name, CvrNumber? cvr = null, bool isActive = true)
    {
        return new SupplierIdentity
        {
            Gln = gln,
            Name = name,
            Cvr = cvr,
            IsActive = isActive,
        };
    }

    /// <summary>Create a legacy identity (acquired supplier, corrections only)</summary>
    public static SupplierIdentity CreateLegacy(GlnNumber gln, string name, CvrNumber? cvr = null)
    {
        return Create(gln, name, cvr, isActive: false);
    }

    public void UpdateName(string name)
    {
        Name = name;
        MarkUpdated();
    }

    public void UpdateCvr(CvrNumber? cvr)
    {
        Cvr = cvr;
        MarkUpdated();
    }

    public void Deactivate()
    {
        IsActive = false;
        MarkUpdated();
    }

    public void Activate()
    {
        IsActive = true;
        MarkUpdated();
    }

    /// <summary>Archive — no more corrections expected for any metering points on this GLN.</summary>
    public void Archive()
    {
        IsActive = false;
        IsArchived = true;
        MarkUpdated();
    }

    public void Unarchive()
    {
        IsArchived = false;
        MarkUpdated();
    }
}
