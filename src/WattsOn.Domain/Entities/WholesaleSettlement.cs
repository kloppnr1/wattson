using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// WholesaleSettlement — DataHub's official settlement calculations (BRS-027).
/// Received for reconciliation against our own settlement engine results.
/// </summary>
public class WholesaleSettlement : Entity
{
    /// <summary>Grid area this settlement covers</summary>
    public string GridArea { get; private set; } = null!;

    /// <summary>D05=Wholesale, D32=Correction</summary>
    public string BusinessReason { get; private set; } = null!;

    /// <summary>Settlement period</summary>
    public Period Period { get; private set; } = null!;

    /// <summary>Total energy in kWh</summary>
    public decimal TotalEnergyKwh { get; private set; }

    /// <summary>Total amount in DKK</summary>
    public decimal TotalAmountDkk { get; private set; }

    /// <summary>Currency</summary>
    public string Currency { get; private set; } = "DKK";

    /// <summary>Resolution of underlying data</summary>
    public Resolution Resolution { get; private set; }

    /// <summary>When received from DataHub</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>DataHub transaction ID</summary>
    public string? TransactionId { get; private set; }

    /// <summary>Line items in the wholesale settlement</summary>
    private readonly List<WholesaleSettlementLine> _lines = new();
    public IReadOnlyList<WholesaleSettlementLine> Lines => _lines.AsReadOnly();

    private WholesaleSettlement() { }

    public static WholesaleSettlement Create(
        string gridArea,
        string businessReason,
        Period period,
        Resolution resolution,
        string? transactionId)
    {
        return new WholesaleSettlement
        {
            GridArea = gridArea,
            BusinessReason = businessReason,
            Period = period,
            Resolution = resolution,
            TotalEnergyKwh = 0,
            TotalAmountDkk = 0,
            ReceivedAt = DateTimeOffset.UtcNow,
            TransactionId = transactionId
        };
    }

    public void AddLine(string chargeId, string chargeType, string ownerGln,
        decimal energyKwh, decimal amountDkk, string description)
    {
        _lines.Add(WholesaleSettlementLine.Create(Id, chargeId, chargeType, ownerGln, energyKwh, amountDkk, description));
        TotalEnergyKwh += energyKwh;
        TotalAmountDkk += amountDkk;
    }
}

public class WholesaleSettlementLine : Entity
{
    public Guid WholesaleSettlementId { get; private set; }
    public string ChargeId { get; private set; } = null!;
    public string ChargeType { get; private set; } = null!;
    public string OwnerGln { get; private set; } = null!;
    public decimal EnergyKwh { get; private set; }
    public decimal AmountDkk { get; private set; }
    public string Description { get; private set; } = null!;

    private WholesaleSettlementLine() { }

    public static WholesaleSettlementLine Create(
        Guid parentId, string chargeId, string chargeType, string ownerGln,
        decimal energyKwh, decimal amountDkk, string description)
    {
        return new WholesaleSettlementLine
        {
            WholesaleSettlementId = parentId,
            ChargeId = chargeId,
            ChargeType = chargeType,
            OwnerGln = ownerGln,
            EnergyKwh = energyKwh,
            AmountDkk = amountDkk,
            Description = description
        };
    }
}
