using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs021HandlerTests
{
    private static readonly DateTimeOffset Jan15 = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jan16 = new(2026, 1, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid MeteringPointId = Guid.NewGuid();

    // --- Helpers ---

    private static List<Brs021Handler.ObservationData> CreateHourlyObservations(
        DateTimeOffset start, int count, decimal baseKwh = 1.0m, QuantityQuality quality = QuantityQuality.Measured)
    {
        var observations = new List<Brs021Handler.ObservationData>();
        for (var i = 0; i < count; i++)
        {
            observations.Add(new Brs021Handler.ObservationData(
                start.AddHours(i),
                Math.Round(baseKwh + i * 0.1m, 3),
                quality));
        }
        return observations;
    }

    private static TimeSeries CreateExistingTimeSeries(
        Guid meteringPointId, DateTimeOffset start, DateTimeOffset end,
        Resolution resolution = Resolution.PT1H, int version = 1)
    {
        var period = Period.Create(start, end);
        return TimeSeries.Create(meteringPointId, period, resolution, version, "existing-txn");
    }

    // =====================================================
    // ProcessMeteredData
    // =====================================================

    [Fact]
    public void ProcessMeteredData_CreatesNewTimeSeries()
    {
        var observations = CreateHourlyObservations(Jan15, 24, baseKwh: 1.5m);

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, "TXN-001", existingLatest: null);

        Assert.NotNull(result.TimeSeries);
        Assert.Null(result.SupersededVersion);
        Assert.Equal(1, result.TimeSeries.Version);
        Assert.True(result.TimeSeries.IsLatest);
        Assert.Equal(MeteringPointId, result.TimeSeries.MeteringPointId);
        Assert.Equal(Jan15, result.TimeSeries.Period.Start);
        Assert.Equal(Jan16, result.TimeSeries.Period.End);
        Assert.Equal(Resolution.PT1H, result.TimeSeries.Resolution);
        Assert.Equal(24, result.TimeSeries.Observations.Count);
    }

    [Fact]
    public void ProcessMeteredData_VersionsExistingTimeSeries()
    {
        var existing = CreateExistingTimeSeries(MeteringPointId, Jan15, Jan16, version: 1);
        Assert.True(existing.IsLatest);

        var observations = CreateHourlyObservations(Jan15, 24, baseKwh: 2.0m);

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, "TXN-002", existingLatest: existing);

        Assert.Equal(2, result.TimeSeries.Version);
        Assert.True(result.TimeSeries.IsLatest);
        Assert.NotNull(result.SupersededVersion);
        Assert.Same(existing, result.SupersededVersion);
        Assert.False(existing.IsLatest);
    }

    [Fact]
    public void ProcessMeteredData_CalculatesTotalEnergy()
    {
        // 3 observations: 1.0 + 1.5 + 2.0 = 4.5 kWh
        var observations = new List<Brs021Handler.ObservationData>
        {
            new(Jan15, 1.0m, QuantityQuality.Measured),
            new(Jan15.AddHours(1), 1.5m, QuantityQuality.Measured),
            new(Jan15.AddHours(2), 2.0m, QuantityQuality.Measured),
        };

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, "TXN-003", existingLatest: null);

        Assert.Equal(4.5m, result.TimeSeries.TotalEnergy.Value);
    }

    [Fact]
    public void ProcessMeteredData_PreservesObservationQuality()
    {
        var observations = new List<Brs021Handler.ObservationData>
        {
            new(Jan15, 1.0m, QuantityQuality.Measured),
            new(Jan15.AddHours(1), 1.5m, QuantityQuality.Estimated),
            new(Jan15.AddHours(2), 0.0m, QuantityQuality.NotAvailable),
            new(Jan15.AddHours(3), 2.0m, QuantityQuality.Calculated),
        };

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, "TXN-004", existingLatest: null);

        Assert.Equal(QuantityQuality.Measured, result.TimeSeries.Observations[0].Quality);
        Assert.Equal(QuantityQuality.Estimated, result.TimeSeries.Observations[1].Quality);
        Assert.Equal(QuantityQuality.NotAvailable, result.TimeSeries.Observations[2].Quality);
        Assert.Equal(QuantityQuality.Calculated, result.TimeSeries.Observations[3].Quality);
    }

    [Fact]
    public void ProcessMeteredData_MultipleVersions()
    {
        // v1
        var v1 = CreateExistingTimeSeries(MeteringPointId, Jan15, Jan16, version: 1);
        var obs1 = CreateHourlyObservations(Jan15, 24, baseKwh: 1.0m);
        var result1 = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            obs1, "TXN-V1", existingLatest: null);
        Assert.Equal(1, result1.TimeSeries.Version);

        // v2 supersedes v1
        var obs2 = CreateHourlyObservations(Jan15, 24, baseKwh: 1.5m);
        var result2 = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            obs2, "TXN-V2", existingLatest: result1.TimeSeries);
        Assert.Equal(2, result2.TimeSeries.Version);
        Assert.False(result1.TimeSeries.IsLatest);

        // v3 supersedes v2
        var obs3 = CreateHourlyObservations(Jan15, 24, baseKwh: 2.0m);
        var result3 = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            obs3, "TXN-V3", existingLatest: result2.TimeSeries);
        Assert.Equal(3, result3.TimeSeries.Version);
        Assert.False(result2.TimeSeries.IsLatest);
        Assert.True(result3.TimeSeries.IsLatest);
    }

    [Fact]
    public void ProcessMeteredData_EmptyObservations()
    {
        var observations = new List<Brs021Handler.ObservationData>();

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, "TXN-EMPTY", existingLatest: null);

        Assert.NotNull(result.TimeSeries);
        Assert.Empty(result.TimeSeries.Observations);
        Assert.Equal(0m, result.TimeSeries.TotalEnergy.Value);
    }

    [Fact]
    public void ProcessMeteredData_QuarterHourResolution()
    {
        // 96 quarter-hour observations in a day
        var observations = new List<Brs021Handler.ObservationData>();
        for (var i = 0; i < 96; i++)
        {
            observations.Add(new Brs021Handler.ObservationData(
                Jan15.AddMinutes(i * 15), 0.25m, QuantityQuality.Measured));
        }

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT15M,
            observations, "TXN-15M", existingLatest: null);

        Assert.Equal(Resolution.PT15M, result.TimeSeries.Resolution);
        Assert.Equal(96, result.TimeSeries.Observations.Count);
        Assert.Equal(24.0m, result.TimeSeries.TotalEnergy.Value);
    }

    [Fact]
    public void ProcessMeteredData_SetsTransactionId()
    {
        var observations = CreateHourlyObservations(Jan15, 3);

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, "MY-TRANSACTION-123", existingLatest: null);

        Assert.Equal("MY-TRANSACTION-123", result.TimeSeries.TransactionId);
    }

    [Fact]
    public void ProcessMeteredData_SupersededVersionMarkedNotLatest()
    {
        var existing = CreateExistingTimeSeries(MeteringPointId, Jan15, Jan16, version: 1);
        Assert.True(existing.IsLatest);

        var observations = CreateHourlyObservations(Jan15, 24);

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, "TXN-NEW", existingLatest: existing);

        Assert.False(existing.IsLatest);
        Assert.True(result.TimeSeries.IsLatest);
        Assert.Same(existing, result.SupersededVersion);
    }

    // =====================================================
    // MapQuantityStatus
    // =====================================================

    [Theory]
    [InlineData(null, QuantityQuality.Measured)]
    [InlineData("", QuantityQuality.Measured)]
    [InlineData("A01", QuantityQuality.Measured)]
    [InlineData("A02", QuantityQuality.Estimated)]
    [InlineData("A03", QuantityQuality.Calculated)]
    [InlineData("A04", QuantityQuality.NotAvailable)]
    [InlineData("A05", QuantityQuality.Revised)]
    [InlineData("E01", QuantityQuality.Adjusted)]
    [InlineData("X99", QuantityQuality.Estimated)]
    [InlineData("UNKNOWN", QuantityQuality.Estimated)]
    public void MapQuantityStatus_MapsAllCodes(string? statusCode, QuantityQuality expected)
    {
        var result = Brs021Handler.MapQuantityStatus(statusCode);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProcessMeteredData_NullTransactionId_Allowed()
    {
        var observations = CreateHourlyObservations(Jan15, 3);

        var result = Brs021Handler.ProcessMeteredData(
            MeteringPointId, Jan15, Jan16, Resolution.PT1H,
            observations, transactionId: null, existingLatest: null);

        Assert.Null(result.TimeSeries.TransactionId);
        Assert.Equal(1, result.TimeSeries.Version);
    }
}
