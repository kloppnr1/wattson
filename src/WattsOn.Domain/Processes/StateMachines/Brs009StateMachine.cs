using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Processes.StateMachines;

/// <summary>
/// State machine for BRS-009: Tilflytning (Move-In).
/// 
/// Initiator flow (we're registering the move-in):
///   Created → Submitted → Confirmed → AwaitingCustomerData → Completed
///                       → Rejected
/// 
/// Always initiator for this process (only supplier initiates move-in).
/// DataHub confirms and notifies grid company.
/// Must send customer data (BRS-015) within 15 business days.
/// </summary>
public class Brs009StateMachine : IStateMachine
{
    public const string Created = "Created";
    public const string Submitted = "Submitted";
    public const string Confirmed = "Confirmed";
    public const string AwaitingCustomerData = "AwaitingCustomerData";
    public const string Completed = "Completed";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string Failed = "Failed";

    private static readonly Dictionary<string, List<string>> Transitions = new()
    {
        [Created] = [Submitted, Cancelled],
        [Submitted] = [Confirmed, Rejected, Failed],
        [Confirmed] = [AwaitingCustomerData, Completed],
        [AwaitingCustomerData] = [Completed, Failed]
    };

    private static readonly HashSet<string> TerminalStates = [Completed, Rejected, Cancelled, Failed];

    public string InitialState => Created;

    public bool CanTransition(string fromState, string toState) =>
        Transitions.TryGetValue(fromState, out var validStates) && validStates.Contains(toState);

    public IReadOnlyList<string> GetValidTransitions(string currentState) =>
        Transitions.TryGetValue(currentState, out var validStates) ? validStates : [];

    public bool IsTerminal(string state) => TerminalStates.Contains(state);
}
