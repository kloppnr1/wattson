using WattsOn.Domain.Entities;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Entities;

public class LeveranceTests
{
    [Fact]
    public void Create_SetsAllProperties()
    {
        var mpId = Guid.NewGuid();
        var kundeId = Guid.NewGuid();
        var aktørId = Guid.NewGuid();
        var period = Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var leverance = Leverance.Create(mpId, kundeId, aktørId, period);

        Assert.Equal(mpId, leverance.MålepunktId);
        Assert.Equal(kundeId, leverance.KundeId);
        Assert.Equal(aktørId, leverance.AktørId);
        Assert.True(leverance.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void IsActive_OpenEndedStartedInPast_ReturnsTrue()
    {
        var period = Period.From(DateTimeOffset.UtcNow.AddDays(-10));
        var leverance = Leverance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), period);

        Assert.True(leverance.IsActive);
    }

    [Fact]
    public void EndSupply_ClosesThePeriod()
    {
        var period = Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var leverance = Leverance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), period);

        var endDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        leverance.EndSupply(endDate);

        Assert.False(leverance.SupplyPeriod.IsOpenEnded);
        Assert.Equal(endDate, leverance.SupplyPeriod.End);
    }

    [Fact]
    public void EndSupply_SetsEndedByProcessId()
    {
        var period = Period.From(DateTimeOffset.UtcNow.AddDays(-10));
        var leverance = Leverance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), period);

        var processId = Guid.NewGuid();
        leverance.EndSupply(DateTimeOffset.UtcNow.AddDays(1), processId);

        Assert.Equal(processId, leverance.EndedByProcessId);
    }
}
