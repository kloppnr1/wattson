using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-027 — Anmodning om engrosydelser/engrosfiksering.
/// Processes RSM-019/D05 messages with wholesale settlement results from DataHub.
/// Used for reconciliation against our own settlement engine results.
/// </summary>
public static class Brs027Handler
{
    public record WholesaleSettlementResult(WholesaleSettlement Settlement);

    public record SettlementLineData(
        string ChargeId,
        string ChargeType,
        string OwnerGln,
        decimal EnergyKwh,
        decimal AmountDkk,
        string Description);

    public static WholesaleSettlementResult ProcessWholesaleSettlement(
        string gridArea,
        string businessReason,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Resolution resolution,
        string? transactionId,
        IReadOnlyList<SettlementLineData> lines)
    {
        var period = Period.Create(periodStart, periodEnd);
        var settlement = WholesaleSettlement.Create(
            gridArea, businessReason, period, resolution, transactionId);

        foreach (var line in lines)
        {
            settlement.AddLine(
                line.ChargeId, line.ChargeType, line.OwnerGln,
                line.EnergyKwh, line.AmountDkk, line.Description);
        }

        return new WholesaleSettlementResult(settlement);
    }
}
