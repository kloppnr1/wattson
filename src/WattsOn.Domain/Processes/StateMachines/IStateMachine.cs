namespace WattsOn.Domain.Processes.StateMachines;

/// <summary>
/// Interface for BRS process state machines.
/// Each BRS process type implements its own state machine with specific states and transitions.
/// </summary>
public interface IStateMachine
{
    /// <summary>Get the initial state for this process</summary>
    string InitialState { get; }

    /// <summary>Check if a transition from one state to another is valid</summary>
    bool CanTransition(string fromState, string toState);

    /// <summary>Get all valid transitions from the current state</summary>
    IReadOnlyList<string> GetValidTransitions(string currentState);

    /// <summary>Check if the given state is a terminal state</summary>
    bool IsTerminal(string state);
}
