using WattsOn.Domain.Common;

namespace WattsOn.Domain.ValueObjects;

/// <summary>
/// A time period with start (inclusive) and end (exclusive).
/// Maps to PostgreSQL tstzrange.
/// Used for supply periods, settlement periods, price validity, etc.
/// </summary>
public class Period : ValueObject
{
    /// <summary>Start of period (inclusive)</summary>
    public DateTimeOffset Start { get; }

    /// <summary>End of period (exclusive). Null = open-ended / ongoing.</summary>
    public DateTimeOffset? End { get; }

    private Period(DateTimeOffset start, DateTimeOffset? end)
    {
        Start = start;
        End = end;
    }

    public static Period Create(DateTimeOffset start, DateTimeOffset? end = null)
    {
        if (end.HasValue && end.Value <= start)
            throw new ArgumentException("Period end must be after start.", nameof(end));

        return new Period(start, end);
    }

    /// <summary>Open-ended period (no end date)</summary>
    public static Period From(DateTimeOffset start) => new(start, null);

    /// <summary>Check if this period is currently active</summary>
    public bool IsActive(DateTimeOffset? at = null)
    {
        var now = at ?? DateTimeOffset.UtcNow;
        return Start <= now && (!End.HasValue || End.Value > now);
    }

    /// <summary>Check if this period overlaps with another</summary>
    public bool Overlaps(Period other)
    {
        // [A.Start, A.End) overlaps [B.Start, B.End) if A.Start < B.End AND B.Start < A.End
        var thisEnd = End ?? DateTimeOffset.MaxValue;
        var otherEnd = other.End ?? DateTimeOffset.MaxValue;
        return Start < otherEnd && other.Start < thisEnd;
    }

    /// <summary>Check if a point in time falls within this period</summary>
    public bool Contains(DateTimeOffset point)
    {
        return Start <= point && (!End.HasValue || End.Value > point);
    }

    /// <summary>Close an open-ended period</summary>
    public Period ClosedAt(DateTimeOffset end) => Create(Start, end);

    /// <summary>Duration of the period, or null if open-ended</summary>
    public TimeSpan? Duration => End.HasValue ? End.Value - Start : null;

    public bool IsOpenEnded => !End.HasValue;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Start;
        yield return End;
    }

    public override string ToString() =>
        End.HasValue
            ? $"[{Start:yyyy-MM-dd}, {End:yyyy-MM-dd})"
            : $"[{Start:yyyy-MM-dd}, →)";
}
