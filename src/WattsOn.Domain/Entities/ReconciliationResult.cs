using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Reconciliation status — outcome of comparing our settlements vs DataHub's.
/// </summary>
public enum ReconciliationStatus
{
    /// <summary>Awaiting DataHub data to compare</summary>
    Pending,

    /// <summary>All amounts match within tolerance</summary>
    Matched,

    /// <summary>Discrepancies found between our data and DataHub's</summary>
    Discrepancy
}

/// <summary>
/// ReconciliationResult — compares our settlement calculations against DataHub's
/// wholesale settlement (WholesaleSettlement entity) for a grid area + period.
///
/// Created by running reconciliation after receiving BRS-027 wholesale settlement data.
/// </summary>
public class ReconciliationResult : Entity
{
    /// <summary>Default tolerance for matching amounts (0.01 DKK = 1 øre)</summary>
    public const decimal DefaultToleranceDkk = 0.01m;

    /// <summary>Grid area being reconciled</summary>
    public string GridArea { get; private set; } = null!;

    /// <summary>Period being reconciled</summary>
    public Period Period { get; private set; } = null!;

    /// <summary>Our calculated total in DKK</summary>
    public decimal OurTotalDkk { get; private set; }

    /// <summary>DataHub's total in DKK</summary>
    public decimal DataHubTotalDkk { get; private set; }

    /// <summary>Absolute difference (Our - DataHub)</summary>
    public decimal DifferenceDkk { get; private set; }

    /// <summary>Percentage difference relative to DataHub total. 0 if DataHub total is 0.</summary>
    public decimal DifferencePercent { get; private set; }

    /// <summary>Reconciliation status</summary>
    public ReconciliationStatus Status { get; private set; }

    /// <summary>When the reconciliation was performed</summary>
    public DateTimeOffset ReconciliationDate { get; private set; }

    /// <summary>Associated wholesale settlement ID (if available)</summary>
    public Guid? WholesaleSettlementId { get; private set; }

    /// <summary>Free-text notes about the reconciliation</summary>
    public string? Notes { get; private set; }

    /// <summary>Line-level comparison details</summary>
    private readonly List<ReconciliationLine> _lines = new();
    public IReadOnlyList<ReconciliationLine> Lines => _lines.AsReadOnly();

    private ReconciliationResult() { }

    /// <summary>
    /// Create a pending reconciliation result (no DataHub data yet).
    /// </summary>
    public static ReconciliationResult CreatePending(
        string gridArea,
        Period period,
        decimal ourTotalDkk)
    {
        return new ReconciliationResult
        {
            GridArea = gridArea,
            Period = period,
            OurTotalDkk = ourTotalDkk,
            DataHubTotalDkk = 0,
            DifferenceDkk = ourTotalDkk,
            DifferencePercent = 0,
            Status = ReconciliationStatus.Pending,
            ReconciliationDate = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Reconcile our settlement line data against DataHub wholesale settlement.
    /// Performs line-by-line comparison and computes aggregate differences.
    /// </summary>
    /// <param name="gridArea">Grid area code</param>
    /// <param name="period">Reconciliation period</param>
    /// <param name="ourLines">Our settlement lines (ChargeId → Amount)</param>
    /// <param name="wholesaleSettlement">DataHub's wholesale settlement</param>
    /// <param name="toleranceDkk">Tolerance for amount matching (default 0.01 DKK)</param>
    public static ReconciliationResult Reconcile(
        string gridArea,
        Period period,
        IReadOnlyList<ReconciliationLineInput> ourLines,
        WholesaleSettlement? wholesaleSettlement,
        decimal toleranceDkk = DefaultToleranceDkk)
    {
        if (wholesaleSettlement is null)
        {
            var ourTotal = ourLines.Sum(l => l.Amount);
            return CreatePending(gridArea, period, ourTotal);
        }

        var result = new ReconciliationResult
        {
            GridArea = gridArea,
            Period = period,
            WholesaleSettlementId = wholesaleSettlement.Id,
            ReconciliationDate = DateTimeOffset.UtcNow,
        };

        // Build lookup from DataHub lines by ChargeId+ChargeType key
        var dhLookup = wholesaleSettlement.Lines
            .ToDictionary(l => $"{l.ChargeId}|{l.ChargeType}", l => l);

        var processedDhKeys = new HashSet<string>();

        // Compare each of our lines against DataHub
        foreach (var ourLine in ourLines)
        {
            var key = $"{ourLine.ChargeId}|{ourLine.ChargeType}";
            if (dhLookup.TryGetValue(key, out var dhLine))
            {
                processedDhKeys.Add(key);
                result._lines.Add(ReconciliationLine.Create(
                    result.Id,
                    ourLine.ChargeId,
                    ourLine.ChargeType,
                    ourLine.Amount,
                    dhLine.AmountDkk));
            }
            else
            {
                // We have a charge that DataHub doesn't
                result._lines.Add(ReconciliationLine.Create(
                    result.Id,
                    ourLine.ChargeId,
                    ourLine.ChargeType,
                    ourLine.Amount,
                    0m));
            }
        }

        // Find DataHub lines we don't have
        foreach (var dhLine in wholesaleSettlement.Lines)
        {
            var key = $"{dhLine.ChargeId}|{dhLine.ChargeType}";
            if (!processedDhKeys.Contains(key))
            {
                result._lines.Add(ReconciliationLine.Create(
                    result.Id,
                    dhLine.ChargeId,
                    dhLine.ChargeType,
                    0m,
                    dhLine.AmountDkk));
            }
        }

        // Compute totals
        result.OurTotalDkk = result._lines.Sum(l => l.OurAmount);
        result.DataHubTotalDkk = result._lines.Sum(l => l.DataHubAmount);
        result.DifferenceDkk = result.OurTotalDkk - result.DataHubTotalDkk;
        result.DifferencePercent = result.DataHubTotalDkk != 0
            ? Math.Round(result.DifferenceDkk / result.DataHubTotalDkk * 100m, 4)
            : 0m;

        // Determine status — matched if all lines within tolerance
        var hasDiscrepancy = result._lines.Any(l => Math.Abs(l.Difference) > toleranceDkk);
        result.Status = hasDiscrepancy ? ReconciliationStatus.Discrepancy : ReconciliationStatus.Matched;

        return result;
    }

    public void SetNotes(string notes)
    {
        Notes = notes;
        MarkUpdated();
    }
}

/// <summary>
/// Input for reconciliation — represents one of our settlement charges.
/// </summary>
public record ReconciliationLineInput(string ChargeId, string ChargeType, decimal Amount);

/// <summary>
/// ReconciliationLine — comparison of a single charge between our data and DataHub's.
/// </summary>
public class ReconciliationLine : Entity
{
    public Guid ReconciliationResultId { get; private set; }
    public string ChargeId { get; private set; } = null!;
    public string ChargeType { get; private set; } = null!;
    public decimal OurAmount { get; private set; }
    public decimal DataHubAmount { get; private set; }
    public decimal Difference { get; private set; }

    private ReconciliationLine() { }

    public static ReconciliationLine Create(
        Guid reconciliationResultId,
        string chargeId,
        string chargeType,
        decimal ourAmount,
        decimal dataHubAmount)
    {
        return new ReconciliationLine
        {
            ReconciliationResultId = reconciliationResultId,
            ChargeId = chargeId,
            ChargeType = chargeType,
            OurAmount = ourAmount,
            DataHubAmount = dataHubAmount,
            Difference = ourAmount - dataHubAmount
        };
    }
}
