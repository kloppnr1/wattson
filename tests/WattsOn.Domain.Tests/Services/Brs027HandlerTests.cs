using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;

namespace WattsOn.Domain.Tests.Services;

public class Brs027HandlerTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProcessWholesaleSettlement_CreatesWithCorrectFields()
    {
        var lines = new List<Brs027Handler.SettlementLineData>
        {
            new("DT-001", "D03", "5790001330552", 1000.0m, 500.0m, "Nettarif"),
        };

        var result = Brs027Handler.ProcessWholesaleSettlement(
            "DK1", "D05", Jan1, Feb1, Resolution.PT1H, "TXN-WS-001", lines);

        Assert.NotNull(result.Settlement);
        Assert.Equal("DK1", result.Settlement.GridArea);
        Assert.Equal("D05", result.Settlement.BusinessReason);
        Assert.Equal(Jan1, result.Settlement.Period.Start);
        Assert.Equal(Feb1, result.Settlement.Period.End);
        Assert.Equal(Resolution.PT1H, result.Settlement.Resolution);
        Assert.Equal("TXN-WS-001", result.Settlement.TransactionId);
        Assert.Equal("DKK", result.Settlement.Currency);
    }

    [Fact]
    public void ProcessWholesaleSettlement_LinesAddedAndTotalsAccumulated()
    {
        var lines = new List<Brs027Handler.SettlementLineData>
        {
            new("DT-001", "D03", "5790001330552", 1000.0m, 500.0m, "Nettarif"),
        };

        var result = Brs027Handler.ProcessWholesaleSettlement(
            "DK1", "D05", Jan1, Feb1, Resolution.PT1H, "TXN-WS-002", lines);

        Assert.Single(result.Settlement.Lines);
        Assert.Equal(1000.0m, result.Settlement.TotalEnergyKwh);
        Assert.Equal(500.0m, result.Settlement.TotalAmountDkk);
    }

    [Fact]
    public void ProcessWholesaleSettlement_MultipleLinesSum()
    {
        var lines = new List<Brs027Handler.SettlementLineData>
        {
            new("DT-001", "D03", "5790001330552", 500.0m, 250.0m, "Nettarif"),
            new("DT-002", "D01", "5790001330552", 300.0m, 150.0m, "Systemtarif"),
            new("EA-001", "D02", "5790000432752", 200.0m, 100.0m, "Elafgift"),
        };

        var result = Brs027Handler.ProcessWholesaleSettlement(
            "DK1", "D05", Jan1, Feb1, Resolution.PT1H, "TXN-WS-003", lines);

        Assert.Equal(3, result.Settlement.Lines.Count);
        Assert.Equal(1000.0m, result.Settlement.TotalEnergyKwh);
        Assert.Equal(500.0m, result.Settlement.TotalAmountDkk);
    }

    [Fact]
    public void ProcessWholesaleSettlement_EmptySettlement_HasZeroTotals()
    {
        var lines = new List<Brs027Handler.SettlementLineData>();

        var result = Brs027Handler.ProcessWholesaleSettlement(
            "DK2", "D32", Jan1, Feb1, Resolution.PT1H, "TXN-WS-004", lines);

        Assert.NotNull(result.Settlement);
        Assert.Empty(result.Settlement.Lines);
        Assert.Equal(0m, result.Settlement.TotalEnergyKwh);
        Assert.Equal(0m, result.Settlement.TotalAmountDkk);
    }

    [Fact]
    public void ProcessWholesaleSettlement_TransactionIdPreserved()
    {
        var lines = new List<Brs027Handler.SettlementLineData>();

        var result = Brs027Handler.ProcessWholesaleSettlement(
            "DK1", "D05", Jan1, Feb1, Resolution.PT1H, "MY-UNIQUE-TXN", lines);

        Assert.Equal("MY-UNIQUE-TXN", result.Settlement.TransactionId);

        // Also test null transaction id
        var result2 = Brs027Handler.ProcessWholesaleSettlement(
            "DK1", "D05", Jan1, Feb1, Resolution.PT1H, null, lines);

        Assert.Null(result2.Settlement.TransactionId);
    }
}
