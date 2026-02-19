using WattsOn.Domain.Common;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Entities;

/// <summary>
/// Persisted record of why a settlement could not be created.
/// Created by SettlementWorker when validation fails (missing price elements,
/// no price points, etc.). Displayed in the UI so backoffice staff can act on it.
///
/// Lifecycle:
///   Open → Resolved (when the issue is fixed and settlement succeeds)
///        → Dismissed (manually acknowledged by staff)
/// </summary>
public class SettlementIssue : Entity
{
    public Guid MeteringPointId { get; private set; }
    public Guid TimeSeriesId { get; private set; }
    public int TimeSeriesVersion { get; private set; }

    /// <summary>Settlement period that couldn't be calculated</summary>
    public Period Period { get; private set; } = null!;

    /// <summary>Issue category</summary>
    public SettlementIssueType IssueType { get; private set; }

    /// <summary>Human-readable summary (Danish)</summary>
    public string Message { get; private set; } = null!;

    /// <summary>Detailed list of missing/incomplete elements</summary>
    public string Details { get; private set; } = null!;

    /// <summary>Current status</summary>
    public SettlementIssueStatus Status { get; private set; }

    /// <summary>When this issue was resolved (settlement succeeded or manually dismissed)</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    private SettlementIssue() { } // EF Core

    public static SettlementIssue CreateMissingPrices(
        Guid meteringPointId,
        Guid timeSeriesId,
        int timeSeriesVersion,
        Period period,
        IReadOnlyList<string> missingElements)
    {
        return new SettlementIssue
        {
            MeteringPointId = meteringPointId,
            TimeSeriesId = timeSeriesId,
            TimeSeriesVersion = timeSeriesVersion,
            Period = period,
            IssueType = SettlementIssueType.MissingPriceElements,
            Message = $"Afregning blokeret: {missingElements.Count} priselement(er) mangler",
            Details = string.Join("\n", missingElements),
            Status = SettlementIssueStatus.Open,
        };
    }

    public static SettlementIssue CreatePriceCoverageGap(
        Guid meteringPointId,
        Guid timeSeriesId,
        int timeSeriesVersion,
        Period period,
        IReadOnlyList<string> coverageIssues)
    {
        return new SettlementIssue
        {
            MeteringPointId = meteringPointId,
            TimeSeriesId = timeSeriesId,
            TimeSeriesVersion = timeSeriesVersion,
            Period = period,
            IssueType = SettlementIssueType.PriceCoverageGap,
            Message = $"Afregning blokeret: {coverageIssues.Count} pris(er) mangler prispunkter i perioden",
            Details = string.Join("\n", coverageIssues),
            Status = SettlementIssueStatus.Open,
        };
    }

    public void Resolve()
    {
        Status = SettlementIssueStatus.Resolved;
        ResolvedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void Dismiss()
    {
        Status = SettlementIssueStatus.Dismissed;
        ResolvedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }
}

public enum SettlementIssueType
{
    /// <summary>One or more mandatory price elements not linked to the metering point</summary>
    MissingPriceElements = 1,

    /// <summary>Price is linked but has no price points covering the settlement period</summary>
    PriceCoverageGap = 2,
}

public enum SettlementIssueStatus
{
    /// <summary>Unresolved — needs attention</summary>
    Open = 1,

    /// <summary>Fixed — settlement has since succeeded</summary>
    Resolved = 2,

    /// <summary>Manually acknowledged by backoffice staff</summary>
    Dismissed = 3,
}
