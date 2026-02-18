using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Entities;

public class TidsserieTests
{
    [Fact]
    public void Create_SetsVersionAndLatest()
    {
        var period = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var ts = Tidsserie.Create(Guid.NewGuid(), period, Resolution.PT1H, version: 1);

        Assert.Equal(1, ts.Version);
        Assert.True(ts.IsLatest);
    }

    [Fact]
    public void AddObservation_AddsToCollection()
    {
        var period = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero));

        var ts = Tidsserie.Create(Guid.NewGuid(), period, Resolution.PT1H, version: 1);

        ts.AddObservation(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EnergyQuantity.Create(1.5m),
            QuantityQuality.Målt);

        Assert.Single(ts.Observations);
        Assert.Equal(1.5m, ts.Observations[0].Quantity.Value);
    }

    [Fact]
    public void TotalEnergy_SumsAllObservations()
    {
        var period = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 1, 3, 0, 0, TimeSpan.Zero));

        var ts = Tidsserie.Create(Guid.NewGuid(), period, Resolution.PT1H, version: 1);

        ts.AddObservations(new[]
        {
            (new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), EnergyQuantity.Create(1.0m), QuantityQuality.Målt),
            (new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero), EnergyQuantity.Create(1.5m), QuantityQuality.Målt),
            (new DateTimeOffset(2026, 1, 1, 2, 0, 0, TimeSpan.Zero), EnergyQuantity.Create(2.0m), QuantityQuality.Målt),
        });

        Assert.Equal(3, ts.Observations.Count);
        Assert.Equal(4.5m, ts.TotalEnergy.Value);
    }

    [Fact]
    public void Supersede_MarksAsNotLatest()
    {
        var period = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        var ts = Tidsserie.Create(Guid.NewGuid(), period, Resolution.PT1H, version: 1);

        Assert.True(ts.IsLatest);
        ts.Supersede();
        Assert.False(ts.IsLatest);
    }
}
