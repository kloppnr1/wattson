using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Afregning — a settlement calculation for a metering point and period.
/// Links specific time series versions and price versions together with the calculated result.
/// An external invoicing system picks up settlements via API, invoices the customer,
/// and confirms back. WattsOn detects DataHub corrections and creates adjustment settlements.
/// </summary>
public class Afregning : Entity
{
    public Guid MålepunktId { get; private set; }
    public Guid LeveranceId { get; private set; }

    /// <summary>Settlement period</summary>
    public Period SettlementPeriod { get; private set; } = null!;

    /// <summary>Time series version used for this settlement</summary>
    public Guid TidsserieId { get; private set; }
    public int TidsserieVersion { get; private set; }

    /// <summary>Total energy consumed in settlement period</summary>
    public EnergyQuantity TotalEnergy { get; private set; } = null!;

    /// <summary>Total amount (sum of all settlement lines)</summary>
    public Money TotalAmount { get; private set; } = null!;

    /// <summary>Whether this is a correction of a previous settlement</summary>
    public bool IsCorrection { get; private set; }

    /// <summary>Previous settlement this corrects (if correction)</summary>
    public Guid? PreviousAfregningId { get; private set; }

    /// <summary>When this settlement was calculated</summary>
    public DateTimeOffset CalculatedAt { get; private set; }

    // --- Invoicing lifecycle (managed by external system) ---

    /// <summary>Lifecycle status: Beregnet → Faktureret → Justeret</summary>
    public AfregningStatus Status { get; private set; }

    /// <summary>Invoice reference from external invoicing system</summary>
    public string? ExternalInvoiceReference { get; private set; }

    /// <summary>When external system confirmed this was invoiced</summary>
    public DateTimeOffset? InvoicedAt { get; private set; }

    /// <summary>Sequential document number for external reference (WO-YYYY-NNNNN)</summary>
    public long DocumentNumber { get; private set; }

    /// <summary>Individual line items of this settlement</summary>
    private readonly List<AfregningLinje> _lines = new();
    public IReadOnlyList<AfregningLinje> Lines => _lines.AsReadOnly();

    // Navigation
    public Målepunkt Målepunkt { get; private set; } = null!;
    public Leverance Leverance { get; private set; } = null!;
    public Tidsserie Tidsserie { get; private set; } = null!;

    private Afregning() { } // EF Core

    public static Afregning Create(
        Guid målepunktId,
        Guid leveranceId,
        Period settlementPeriod,
        Guid tidsserieId,
        int tidsserieVersion,
        EnergyQuantity totalEnergy)
    {
        return new Afregning
        {
            MålepunktId = målepunktId,
            LeveranceId = leveranceId,
            SettlementPeriod = settlementPeriod,
            TidsserieId = tidsserieId,
            TidsserieVersion = tidsserieVersion,
            TotalEnergy = totalEnergy,
            TotalAmount = Money.Zero,
            IsCorrection = false,
            Status = AfregningStatus.Beregnet,
            CalculatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Afregning CreateCorrection(
        Guid målepunktId,
        Guid leveranceId,
        Period settlementPeriod,
        Guid tidsserieId,
        int tidsserieVersion,
        EnergyQuantity totalEnergy,
        Guid previousAfregningId)
    {
        return new Afregning
        {
            MålepunktId = målepunktId,
            LeveranceId = leveranceId,
            SettlementPeriod = settlementPeriod,
            TidsserieId = tidsserieId,
            TidsserieVersion = tidsserieVersion,
            TotalEnergy = totalEnergy,
            TotalAmount = Money.Zero,
            IsCorrection = true,
            PreviousAfregningId = previousAfregningId,
            Status = AfregningStatus.Beregnet,
            CalculatedAt = DateTimeOffset.UtcNow
        };
    }

    public void AddLine(AfregningLinje line)
    {
        _lines.Add(line);
        RecalculateTotal();
    }

    /// <summary>
    /// External invoicing system confirms this settlement has been invoiced.
    /// </summary>
    public void MarkInvoiced(string externalInvoiceReference)
    {
        if (Status != AfregningStatus.Beregnet)
            throw new InvalidOperationException(
                $"Cannot mark as invoiced — status is {Status}, expected {AfregningStatus.Beregnet}");

        Status = AfregningStatus.Faktureret;
        ExternalInvoiceReference = externalInvoiceReference;
        InvoicedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    /// <summary>
    /// Mark that a correction has been created for this settlement.
    /// Called when DataHub data changes affect an already-invoiced settlement.
    /// </summary>
    public void MarkAdjusted()
    {
        if (Status != AfregningStatus.Faktureret)
            throw new InvalidOperationException(
                $"Cannot mark as adjusted — status is {Status}, expected {AfregningStatus.Faktureret}");

        Status = AfregningStatus.Justeret;
        MarkUpdated();
    }

    private void RecalculateTotal()
    {
        TotalAmount = _lines.Aggregate(Money.Zero, (sum, line) => sum + line.Amount);
    }
}

/// <summary>
/// AfregningLinje — a single line item in a settlement.
/// One per charge type (grid tariff, system tariff, etc.)
/// </summary>
public class AfregningLinje : Entity
{
    public Guid AfregningId { get; private set; }
    public Guid PrisId { get; private set; }
    public string Description { get; private set; } = null!;
    public EnergyQuantity Quantity { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public Money Amount { get; private set; } = null!;

    private AfregningLinje() { } // EF Core

    public static AfregningLinje Create(
        Guid afregningId,
        Guid prisId,
        string description,
        EnergyQuantity quantity,
        decimal unitPrice)
    {
        var amount = Money.DKK(quantity.Value * unitPrice);
        return new AfregningLinje
        {
            AfregningId = afregningId,
            PrisId = prisId,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount
        };
    }
}
