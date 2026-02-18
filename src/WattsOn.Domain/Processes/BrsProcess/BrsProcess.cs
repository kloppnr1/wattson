using WattsOn.Domain.Common;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Processes;

/// <summary>
/// BrsProcess — a tracked business process instance.
/// Each BRS process (supplier change, move-in, etc.) gets its own instance.
/// Contains the state machine state and all related metadata.
/// </summary>
public class BrsProcess : Entity
{
    /// <summary>DataHub business transaction ID</summary>
    public string? TransactionId { get; private set; }

    /// <summary>Type of BRS process</summary>
    public ProcessType ProcessType { get; private set; }

    /// <summary>Our role in this process (initiator or recipient)</summary>
    public ProcessRole Role { get; private set; }

    /// <summary>Current high-level status</summary>
    public ProcessStatus Status { get; private set; }

    /// <summary>Current state machine state (process-specific)</summary>
    public string CurrentState { get; private set; } = null!;

    /// <summary>GSRN of the metering point involved</summary>
    public Gsrn? MeteringPointGsrn { get; private set; }

    /// <summary>Effective date of the process</summary>
    public DateTimeOffset? EffectiveDate { get; private set; }

    /// <summary>GLN of the counterpart (e.g., grid company, other supplier)</summary>
    public GlnNumber? CounterpartGln { get; private set; }

    /// <summary>When the process was started</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>When the process was completed or failed</summary>
    public DateTimeOffset? CompletedAt { get; private set; }

    /// <summary>Error message if the process failed</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Serialized process-specific data (JSON)</summary>
    public string? ProcessData { get; private set; }

    /// <summary>History of state transitions</summary>
    private readonly List<ProcessStateTransition> _transitions = new();
    public IReadOnlyList<ProcessStateTransition> Transitions => _transitions.AsReadOnly();

    private BrsProcess() { } // EF Core

    public static BrsProcess Create(
        ProcessType processType,
        ProcessRole role,
        string initialState,
        Gsrn? meteringPointGsrn = null,
        DateTimeOffset? effectiveDate = null,
        GlnNumber? counterpartGln = null,
        string? transactionId = null)
    {
        var process = new BrsProcess
        {
            ProcessType = processType,
            Role = role,
            Status = role == ProcessRole.Initiator ? ProcessStatus.Created : ProcessStatus.Received,
            CurrentState = initialState,
            MeteringPointGsrn = meteringPointGsrn,
            EffectiveDate = effectiveDate,
            CounterpartGln = counterpartGln,
            TransactionId = transactionId,
            StartedAt = DateTimeOffset.UtcNow
        };

        process._transitions.Add(ProcessStateTransition.Create(
            process.Id, "Initial", initialState, "Process created"));

        return process;
    }

    /// <summary>
    /// Transition to a new state. Validates the transition is allowed.
    /// </summary>
    public void TransitionTo(string newState, string? reason = null)
    {
        var oldState = CurrentState;
        CurrentState = newState;
        _transitions.Add(ProcessStateTransition.Create(Id, oldState, newState, reason));
        MarkUpdated();
    }

    public void MarkSubmitted(string transactionId)
    {
        TransactionId = transactionId;
        Status = ProcessStatus.Submitted;
        MarkUpdated();
    }

    public void MarkConfirmed()
    {
        Status = ProcessStatus.Confirmed;
        MarkUpdated();
    }

    public void MarkCompleted()
    {
        Status = ProcessStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkRejected(string reason)
    {
        Status = ProcessStatus.Rejected;
        ErrorMessage = reason;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void MarkFailed(string error)
    {
        Status = ProcessStatus.Failed;
        ErrorMessage = error;
        CompletedAt = DateTimeOffset.UtcNow;
        MarkUpdated();
    }

    public void SetProcessData(string json)
    {
        ProcessData = json;
        MarkUpdated();
    }
}

/// <summary>
/// A state transition record — full audit trail of process state changes.
/// </summary>
public class ProcessStateTransition : Entity
{
    public Guid ProcessId { get; private set; }
    public string FromState { get; private set; } = null!;
    public string ToState { get; private set; } = null!;
    public string? Reason { get; private set; }
    public DateTimeOffset TransitionedAt { get; private set; }

    private ProcessStateTransition() { } // EF Core

    public static ProcessStateTransition Create(Guid processId, string fromState, string toState, string? reason = null)
    {
        return new ProcessStateTransition
        {
            ProcessId = processId,
            FromState = fromState,
            ToState = toState,
            Reason = reason,
            TransitionedAt = DateTimeOffset.UtcNow
        };
    }
}
