using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class ReconciliationResultTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Period TestPeriod = Period.Create(Jan1, Feb1);

    private static WholesaleSettlement CreateWholesaleSettlement(
        params (string chargeId, string chargeType, decimal amount)[] lines)
    {
        var ws = WholesaleSettlement.Create("543", "D05", TestPeriod, Resolution.PT1H, "TXN-001");
        foreach (var (chargeId, chargeType, amount) in lines)
        {
            ws.AddLine(chargeId, chargeType, "5790000000005", 100m, amount, $"Charge {chargeId}");
        }
        return ws;
    }

    [Fact]
    public void Reconcile_PerfectMatch_StatusMatched()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.00m),
            new("ABONNEMENT-001", "D01", 250.00m),
        };

        var ws = CreateWholesaleSettlement(
            ("TARIF-001", "D03", 1000.00m),
            ("ABONNEMENT-001", "D01", 250.00m));

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ReconciliationStatus.Matched, result.Status);
        Assert.Equal(0m, result.DifferenceDkk);
        Assert.Equal(0m, result.DifferencePercent);
        Assert.Equal(1250.00m, result.OurTotalDkk);
        Assert.Equal(1250.00m, result.DataHubTotalDkk);
    }

    [Fact]
    public void Reconcile_SmallDifference_WithinTolerance_Matched()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.005m),
        };

        var ws = CreateWholesaleSettlement(("TARIF-001", "D03", 1000.00m));

        // Default tolerance is 0.01 DKK — 0.005 is within tolerance
        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ReconciliationStatus.Matched, result.Status);
    }

    [Fact]
    public void Reconcile_LargeDifference_BeyondTolerance_Discrepancy()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.00m),
        };

        var ws = CreateWholesaleSettlement(("TARIF-001", "D03", 995.00m));

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ReconciliationStatus.Discrepancy, result.Status);
        Assert.Equal(5.00m, result.DifferenceDkk);
    }

    [Fact]
    public void Reconcile_MissingChargesInDataHub_Discrepancy()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.00m),
            new("TARIF-002", "D03", 500.00m), // DataHub doesn't have this
        };

        var ws = CreateWholesaleSettlement(("TARIF-001", "D03", 1000.00m));

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ReconciliationStatus.Discrepancy, result.Status);
        Assert.Equal(2, result.Lines.Count); // TARIF-001 matched + TARIF-002 ours-only

        var missingLine = result.Lines.Single(l => l.ChargeId == "TARIF-002");
        Assert.Equal(500.00m, missingLine.OurAmount);
        Assert.Equal(0m, missingLine.DataHubAmount);
        Assert.Equal(500.00m, missingLine.Difference);
    }

    [Fact]
    public void Reconcile_MissingChargesInOurData_Discrepancy()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.00m),
        };

        var ws = CreateWholesaleSettlement(
            ("TARIF-001", "D03", 1000.00m),
            ("GEBYR-001", "D02", 100.00m)); // We don't have this

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ReconciliationStatus.Discrepancy, result.Status);
        Assert.Equal(2, result.Lines.Count);

        var missingLine = result.Lines.Single(l => l.ChargeId == "GEBYR-001");
        Assert.Equal(0m, missingLine.OurAmount);
        Assert.Equal(100.00m, missingLine.DataHubAmount);
        Assert.Equal(-100.00m, missingLine.Difference);
    }

    [Fact]
    public void Reconcile_AmountDifferencesPerLine()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.00m),
            new("ABONNEMENT-001", "D01", 280.00m),
        };

        var ws = CreateWholesaleSettlement(
            ("TARIF-001", "D03", 990.00m),
            ("ABONNEMENT-001", "D01", 250.00m));

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        var tarifLine = result.Lines.Single(l => l.ChargeId == "TARIF-001");
        Assert.Equal(10.00m, tarifLine.Difference);

        var abonnementLine = result.Lines.Single(l => l.ChargeId == "ABONNEMENT-001");
        Assert.Equal(30.00m, abonnementLine.Difference);
    }

    [Fact]
    public void Reconcile_PercentageCalculation()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1050.00m),
        };

        var ws = CreateWholesaleSettlement(("TARIF-001", "D03", 1000.00m));

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(50.00m, result.DifferenceDkk);
        Assert.Equal(5.0000m, result.DifferencePercent); // 50/1000 * 100 = 5%
    }

    [Fact]
    public void Reconcile_EmptyOurLines_WithDataHubData_Discrepancy()
    {
        var ourLines = new List<ReconciliationLineInput>();

        var ws = CreateWholesaleSettlement(("TARIF-001", "D03", 1000.00m));

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ReconciliationStatus.Discrepancy, result.Status);
        Assert.Single(result.Lines);
        Assert.Equal(0m, result.OurTotalDkk);
        Assert.Equal(1000.00m, result.DataHubTotalDkk);
    }

    [Fact]
    public void Reconcile_BothEmpty_StatusMatched()
    {
        var ourLines = new List<ReconciliationLineInput>();
        var ws = WholesaleSettlement.Create("543", "D05", TestPeriod, Resolution.PT1H, "TXN-001");

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ReconciliationStatus.Matched, result.Status);
        Assert.Empty(result.Lines);
        Assert.Equal(0m, result.DifferenceDkk);
    }

    [Fact]
    public void Reconcile_NullWholesaleSettlement_StatusPending()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.00m),
        };

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, null);

        Assert.Equal(ReconciliationStatus.Pending, result.Status);
        Assert.Equal(1000.00m, result.OurTotalDkk);
        Assert.Null(result.WholesaleSettlementId);
    }

    [Fact]
    public void Reconcile_PreservesGridAreaAndPeriod()
    {
        var ourLines = new List<ReconciliationLineInput>();
        var ws = WholesaleSettlement.Create("543", "D05", TestPeriod, Resolution.PT1H, null);

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal("543", result.GridArea);
        Assert.Equal(Jan1, result.Period.Start);
        Assert.Equal(Feb1, result.Period.End);
    }

    [Fact]
    public void Reconcile_LinksToWholesaleSettlementId()
    {
        var ourLines = new List<ReconciliationLineInput>();
        var ws = WholesaleSettlement.Create("543", "D05", TestPeriod, Resolution.PT1H, null);

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);

        Assert.Equal(ws.Id, result.WholesaleSettlementId);
    }

    [Fact]
    public void Reconcile_CustomTolerance_Respected()
    {
        var ourLines = new List<ReconciliationLineInput>
        {
            new("TARIF-001", "D03", 1000.50m),
        };

        var ws = CreateWholesaleSettlement(("TARIF-001", "D03", 1000.00m));

        // With large tolerance (1.00 DKK), 0.50 diff should be Matched
        var matched = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws, toleranceDkk: 1.00m);
        Assert.Equal(ReconciliationStatus.Matched, matched.Status);

        // With tight tolerance (0.01 DKK), 0.50 diff should be Discrepancy
        var discrepancy = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws, toleranceDkk: 0.01m);
        Assert.Equal(ReconciliationStatus.Discrepancy, discrepancy.Status);
    }

    [Fact]
    public void SetNotes_UpdatesNotes()
    {
        var ourLines = new List<ReconciliationLineInput>();
        var ws = WholesaleSettlement.Create("543", "D05", TestPeriod, Resolution.PT1H, null);

        var result = ReconciliationResult.Reconcile("543", TestPeriod, ourLines, ws);
        result.SetNotes("Verified manually — differences due to rounding");

        Assert.Equal("Verified manually — differences due to rounding", result.Notes);
    }
}
