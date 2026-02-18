using WattsOn.Domain.Entities;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Entities;

public class SupplyTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var mpId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var period = Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var supply = Supply.Create(mpId, customerId, period);

        Assert.Equal(mpId, supply.MeteringPointId);
        Assert.Equal(customerId, supply.CustomerId);
        Assert.True(supply.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void IsActive_OpenEndedStartedInPast_ReturnsTrue()
    {
        var period = Period.From(DateTimeOffset.UtcNow.AddDays(-10));
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(), period);

        Assert.True(supply.IsActive);
    }

    [Fact]
    public void EndSupply_ClosesThePeriod()
    {
        var period = Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(), period);

        var endDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        supply.EndSupply(endDate);

        Assert.False(supply.SupplyPeriod.IsOpenEnded);
        Assert.Equal(endDate, supply.SupplyPeriod.End);
    }

    [Fact]
    public void EndSupply_SetsEndedByProcessId()
    {
        var period = Period.From(DateTimeOffset.UtcNow.AddDays(-10));
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(), period);

        var processId = Guid.NewGuid();
        supply.EndSupply(DateTimeOffset.UtcNow.AddDays(1), processId);

        Assert.Equal(processId, supply.EndedByProcessId);
    }
}
