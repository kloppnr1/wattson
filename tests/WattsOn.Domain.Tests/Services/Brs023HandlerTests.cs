using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;

namespace WattsOn.Domain.Tests.Services;

public class Brs023HandlerTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ProcessAggregatedData_CreatesEntityWithCorrectFields()
    {
        var observations = new List<Brs023Handler.AggregatedObservationData>
        {
            new(Jan1, 100.5m),
            new(Jan1.AddHours(1), 200.0m),
        };

        var result = Brs023Handler.ProcessAggregatedData(
            "DK1", "D03", "E17", "D01",
            Jan1, Feb1, Resolution.PT1H, "Measured", "TXN-001", observations);

        Assert.NotNull(result.TimeSeries);
        Assert.Equal("DK1", result.TimeSeries.GridArea);
        Assert.Equal("D03", result.TimeSeries.BusinessReason);
        Assert.Equal("E17", result.TimeSeries.MeteringPointType);
        Assert.Equal("D01", result.TimeSeries.SettlementMethod);
        Assert.Equal(Jan1, result.TimeSeries.Period.Start);
        Assert.Equal(Feb1, result.TimeSeries.Period.End);
        Assert.Equal(Resolution.PT1H, result.TimeSeries.Resolution);
        Assert.Equal("Measured", result.TimeSeries.QualityStatus);
        Assert.Equal("TXN-001", result.TimeSeries.TransactionId);
    }

    [Fact]
    public void ProcessAggregatedData_ObservationsAddedAndTotalCalculated()
    {
        var observations = new List<Brs023Handler.AggregatedObservationData>
        {
            new(Jan1, 100.0m),
            new(Jan1.AddHours(1), 150.5m),
            new(Jan1.AddHours(2), 200.0m),
        };

        var result = Brs023Handler.ProcessAggregatedData(
            "DK1", "D04", "E17", null,
            Jan1, Feb1, Resolution.PT1H, "Measured", "TXN-002", observations);

        Assert.Equal(3, result.TimeSeries.Observations.Count);
        Assert.Equal(450.5m, result.TimeSeries.TotalEnergyKwh);
    }

    [Theory]
    [InlineData("D03", "Foreløbig aggregering")]
    [InlineData("D04", "Balancefiksering")]
    [InlineData("D05", "Engrosfiksering")]
    [InlineData("D32", "Korrektionsafregning")]
    [InlineData("X99", "X99")]
    [InlineData("UNKNOWN", "UNKNOWN")]
    public void MapBusinessReasonToLabel_MapsAllCodes(string code, string expected)
    {
        Assert.Equal(expected, Brs023Handler.MapBusinessReasonToLabel(code));
    }

    [Fact]
    public void ProcessAggregatedData_EmptyObservations_CreatesEmptyTimeSeries()
    {
        var observations = new List<Brs023Handler.AggregatedObservationData>();

        var result = Brs023Handler.ProcessAggregatedData(
            "DK2", "D05", "E18", null,
            Jan1, Feb1, Resolution.PT1H, "Estimated", "TXN-003", observations);

        Assert.NotNull(result.TimeSeries);
        Assert.Empty(result.TimeSeries.Observations);
        Assert.Equal(0m, result.TimeSeries.TotalEnergyKwh);
    }

    [Fact]
    public void ProcessAggregatedData_QualityStatusPreserved()
    {
        var observations = new List<Brs023Handler.AggregatedObservationData>();

        var result = Brs023Handler.ProcessAggregatedData(
            "DK1", "D03", "E17", null,
            Jan1, Feb1, Resolution.PT1H, "Incomplete", null, observations);

        Assert.Equal("Incomplete", result.TimeSeries.QualityStatus);
        Assert.Null(result.TimeSeries.TransactionId);
    }
}
