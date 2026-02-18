using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Settlement — a settlement calculation for a metering point and period.
/// Links specific time series versions and price versions together with the calculated result.
/// An external invoicing system picks up settlements via API, invoices the customer,
/// and confirms back. WattsOn detects DataHub corrections and creates adjustment settlements.
/// </summary>
public class Settlement : Entity
{
    public Guid MeteringPointId { get; private set; }
    public Guid SupplyId { get; private set; }

    /// <summary>Settlement period</summary>
    public Period SettlementPeriod { get; private set; } = null!;

    /// <summary>Time series version used for this settlement</summary>
    public Guid TimeSeriesId { get; private set; }
    public int TimeSeriesVersion { get; private set; }

    /// <summary>Total energy consumed in settlement period</summary>
    public EnergyQuantity TotalEnergy { get; private set; } = null!;

    /// <summary>Total amount (sum of all settlement lines)</summary>
    public Money TotalAmount { get; private set; } = null!;

    /// <summary>Whether this is a correction of a previous settlement</summary>
    public bool IsCorrection { get; private set; }

    /// <summary>Previous settlement this corrects (if correction)</summary>
    public Guid? PreviousSettlementId { get; private set; }

    /// <summary>When this settlement was calculated</summary>
    public DateTimeOffset CalculatedAt { get; private set; }

    // --- Invoicing lifecycle (managed by external system) ---

    /// <summary>Lifecycle status: Beregnet → Faktureret → Justeret</summary>
    public SettlementStatus Status { get; private set; }

    /// <summary>Invoice reference from external invoicing system</summary>
    public string? ExternalInvoiceReference { get; private set; }

    /// <summary>When external system confirmed this was invoiced</summary>
    public DateTimeOffset? InvoicedAt { get; private set; }

    /// <summary>Sequential document number for external reference (WO-YYYY-NNNNN)</summary>
    public long DocumentNumber { get; private set; }

    /// <summary>Individual line items of this settlement</summary>
    private readonly List<SettlementLine> _lines = new();
    public IReadOnlyList<SettlementLine> Lines => _lines.AsReadOnly();

    // Navigation
    public MeteringPoint MeteringPoint { get; private set; } = null!;
    public Supply Supply { get; private set; } = null!;
    public TimeSeries TimeSeries { get; private set; } = null!;

    private Settlement() { } // EF Core

    public static Settlement Create(
        Guid meteringPointId,
        Guid supplyId,
        Period settlementPeriod,
        Guid timeSeriesId,
        int time_seriesVersion,
        EnergyQuantity totalEnergy)
    {
        return new Settlement
        {
            MeteringPointId = meteringPointId,
            SupplyId = supplyId,
            SettlementPeriod = settlementPeriod,
            TimeSeriesId = timeSeriesId,
            TimeSeriesVersion = time_seriesVersion,
            TotalEnergy = totalEnergy,
            TotalAmount = Money.Zero,
            IsCorrection = false,
            Status = SettlementStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Settlement CreateCorrection(
        Guid meteringPointId,
        Guid supplyId,
        Period settlementPeriod,
        Guid timeSeriesId,
        int time_seriesVersion,
        EnergyQuantity totalEnergy,
        Guid previousSettlementId)
    {
        return new Settlement
        {
            MeteringPointId = meteringPointId,
            SupplyId = supplyId,
            SettlementPeriod = settlementPeriod,
            TimeSeriesId = timeSeriesId,
            TimeSeriesVersion = time_seriesVersion,
            TotalEnergy = totalEnergy,
            TotalAmount = Money.Zero,
            IsCorrection = true,
            PreviousSettlementId = previousSettlementId,
            Status = SettlementStatus.Calculated,
            CalculatedAt = DateTimeOffset.UtcNow
        };
    }

    public void AddLine(SettlementLine line)
    {
        _lines.Add(line);
        RecalculateTotal();
    }

    /// <summary>
    /// External invoicing system confirms this settlement has been invoiced.
    /// </summary>
    public void MarkInvoiced(string externalInvoiceReference)
    {
        if (Status != SettlementStatus.Calculated)
            throw new InvalidOperationException(
                $"Cannot mark as invoiced — status is {Status}, expected {SettlementStatus.Calculated}");

        Status = SettlementStatus.Invoiced;
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
        if (Status != SettlementStatus.Invoiced)
            throw new InvalidOperationException(
                $"Cannot mark as adjusted — status is {Status}, expected {SettlementStatus.Invoiced}");

        Status = SettlementStatus.Adjusted;
        MarkUpdated();
    }

    private void RecalculateTotal()
    {
        TotalAmount = _lines.Aggregate(Money.Zero, (sum, line) => sum + line.Amount);
    }
}

/// <summary>
/// SettlementLine — a single line item in a settlement.
/// One per charge type (grid tariff, system tariff, etc.)
/// </summary>
public class SettlementLine : Entity
{
    public Guid SettlementId { get; private set; }
    public Guid PriceId { get; private set; }
    public string Description { get; private set; } = null!;
    public EnergyQuantity Quantity { get; private set; } = null!;
    public decimal UnitPrice { get; private set; }
    public Money Amount { get; private set; } = null!;

    private SettlementLine() { } // EF Core

    public static SettlementLine Create(
        Guid settlementId,
        Guid priceId,
        string description,
        EnergyQuantity quantity,
        decimal unitPrice)
    {
        var amount = Money.DKK(quantity.Value * unitPrice);
        return new SettlementLine
        {
            SettlementId = settlementId,
            PriceId = priceId,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
            Amount = amount
        };
    }
}
