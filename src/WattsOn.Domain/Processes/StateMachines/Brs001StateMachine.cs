using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Processes.StateMachines;

/// <summary>
/// State machine for BRS-001: Leverandørskift (Change of Supplier).
/// 
/// Initiator flow (we're gaining a customer):
///   Created → Submitted → Confirmed → AwaitingMasterData → Active → Completed
///                       → Rejected
/// 
/// Recipient flow (we're losing a customer):
///   Received → Acknowledged → AwaitingEffectiveDate → FinalSettlement → Completed
///            → Objected
/// </summary>
public class Brs001StateMachine : IStateMachine
{
    // Initiator states (gaining customer)
    public const string Created = "Created";
    public const string Submitted = "Submitted";
    public const string Confirmed = "Confirmed";
    public const string AwaitingMasterData = "AwaitingMasterData";
    public const string Active = "Active";

    // Recipient states (losing customer)
    public const string Received = "Received";
    public const string Acknowledged = "Acknowledged";
    public const string AwaitingEffectiveDate = "AwaitingEffectiveDate";
    public const string FinalSettlement = "FinalSettlement";

    // Shared terminal states
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    private readonly ProcessRole _role;

    private static readonly Dictionary<string, List<string>> InitiatorTransitions = new()
    {
        [Created] = [Submitted, Cancelled],
        [Submitted] = [Confirmed, Rejected, Failed],
        [Confirmed] = [AwaitingMasterData, Active],
        [AwaitingMasterData] = [Active, Failed],
        [Active] = [Completed, Failed]
    };

    private static readonly Dictionary<string, List<string>> RecipientTransitions = new()
    {
        [Received] = [Acknowledged, Cancelled],
        [Acknowledged] = [AwaitingEffectiveDate],
        [AwaitingEffectiveDate] = [FinalSettlement, Cancelled],
        [FinalSettlement] = [Completed, Failed]
    };

    private static readonly HashSet<string> TerminalStates = [Completed, Rejected, Cancelled, Failed];

    public Brs001StateMachine(ProcessRole role) => _role = role;

    public string InitialState => _role == ProcessRole.Initiator ? Created : Received;

    public bool CanTransition(string fromState, string toState)
    {
        var transitions = _role == ProcessRole.Initiator ? InitiatorTransitions : RecipientTransitions;
        return transitions.TryGetValue(fromState, out var validStates) && validStates.Contains(toState);
    }

    public IReadOnlyList<string> GetValidTransitions(string currentState)
    {
        var transitions = _role == ProcessRole.Initiator ? InitiatorTransitions : RecipientTransitions;
        return transitions.TryGetValue(currentState, out var validStates) ? validStates : [];
    }

    public bool IsTerminal(string state) => TerminalStates.Contains(state);
}
