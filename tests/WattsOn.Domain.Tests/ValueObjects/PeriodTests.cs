using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.ValueObjects;

public class PeriodTests
{
    [Fact]
    public void Create_ClosedPeriod_Succeeds()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var period = Period.Create(start, end);

        Assert.Equal(start, period.Start);
        Assert.Equal(end, period.End);
        Assert.False(period.IsOpenEnded);
    }

    [Fact]
    public void Create_OpenEndedPeriod_Succeeds()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var period = Period.From(start);

        Assert.Equal(start, period.Start);
        Assert.Null(period.End);
        Assert.True(period.IsOpenEnded);
    }

    [Fact]
    public void Create_EndBeforeStart_ThrowsArgumentException()
    {
        var start = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Throws<ArgumentException>(() => Period.Create(start, end));
    }

    [Fact]
    public void IsActive_CurrentTimeInPeriod_ReturnsTrue()
    {
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow.AddDays(1);
        var period = Period.Create(start, end);
        Assert.True(period.IsActive());
    }

    [Fact]
    public void IsActive_OpenEndedStartedInPast_ReturnsTrue()
    {
        var period = Period.From(DateTimeOffset.UtcNow.AddDays(-1));
        Assert.True(period.IsActive());
    }

    [Fact]
    public void IsActive_PeriodInFuture_ReturnsFalse()
    {
        var period = Period.Create(
            DateTimeOffset.UtcNow.AddDays(1),
            DateTimeOffset.UtcNow.AddDays(2));
        Assert.False(period.IsActive());
    }

    [Fact]
    public void Overlaps_OverlappingPeriods_ReturnsTrue()
    {
        var p1 = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));
        var p2 = Period.Create(
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(p1.Overlaps(p2));
        Assert.True(p2.Overlaps(p1));
    }

    [Fact]
    public void Overlaps_NonOverlappingPeriods_ReturnsFalse()
    {
        var p1 = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        var p2 = Period.Create(
            new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(p1.Overlaps(p2));
    }

    [Fact]
    public void Overlaps_OpenEndedOverlapsAll()
    {
        var openEnded = Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var closed = Period.Create(
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(openEnded.Overlaps(closed));
    }

    [Fact]
    public void Contains_PointInPeriod_ReturnsTrue()
    {
        var period = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.True(period.Contains(new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ClosedAt_ClosesOpenPeriod()
    {
        var period = Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var closed = period.ClosedAt(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.False(closed.IsOpenEnded);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), closed.End);
    }

    [Fact]
    public void Duration_ClosedPeriod_ReturnsCorrectDuration()
    {
        var period = Period.Create(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(TimeSpan.FromDays(30), period.Duration);
    }

    [Fact]
    public void Duration_OpenEnded_ReturnsNull()
    {
        var period = Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Null(period.Duration);
    }
}
