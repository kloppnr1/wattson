namespace WattsOn.Domain.Enums;

/// <summary>
/// General status for BRS processes.
/// Each process type has its own specific states via state machines,
/// but this provides a high-level lifecycle status.
/// </summary>
public enum ProcessStatus
{
    /// <summary>Process created, not yet submitted or received</summary>
    Created = 1,

    /// <summary>Request submitted to DataHub, awaiting confirmation</summary>
    Submitted = 2,

    /// <summary>Received from DataHub, awaiting processing</summary>
    Received = 3,

    /// <summary>DataHub confirmed receipt / accepted</summary>
    Confirmed = 4,

    /// <summary>Process is active and in progress</summary>
    InProgress = 5,

    /// <summary>Process completed successfully</summary>
    Completed = 6,

    /// <summary>Process was rejected by DataHub or counterpart</summary>
    Rejected = 7,

    /// <summary>Process was cancelled</summary>
    Cancelled = 8,

    /// <summary>Process failed with error</summary>
    Failed = 9
}
