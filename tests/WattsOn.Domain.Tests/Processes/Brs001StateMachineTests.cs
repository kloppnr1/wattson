using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes.StateMachines;

namespace WattsOn.Domain.Tests.Processes;

public class Brs001StateMachineTests
{
    [Fact]
    public void Initiator_InitialState_IsCreated()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        Assert.Equal("Created", sm.InitialState);
    }

    [Fact]
    public void Recipient_InitialState_IsReceived()
    {
        var sm = new Brs001StateMachine(ProcessRole.Recipient);
        Assert.Equal("Received", sm.InitialState);
    }

    [Fact]
    public void Initiator_CanTransition_CreatedToSubmitted()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        Assert.True(sm.CanTransition("Created", "Submitted"));
    }

    [Fact]
    public void Initiator_CannotTransition_CreatedToCompleted()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        Assert.False(sm.CanTransition("Created", "Completed"));
    }

    [Fact]
    public void Initiator_HappyPath_AllTransitionsValid()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);

        Assert.True(sm.CanTransition("Created", "Submitted"));
        Assert.True(sm.CanTransition("Submitted", "Confirmed"));
        Assert.True(sm.CanTransition("Confirmed", "Active"));
        Assert.True(sm.CanTransition("Active", "Completed"));
    }

    [Fact]
    public void Initiator_RejectionPath_Valid()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        Assert.True(sm.CanTransition("Submitted", "Rejected"));
    }

    [Fact]
    public void Recipient_HappyPath_AllTransitionsValid()
    {
        var sm = new Brs001StateMachine(ProcessRole.Recipient);

        Assert.True(sm.CanTransition("Received", "Acknowledged"));
        Assert.True(sm.CanTransition("Acknowledged", "AwaitingEffectiveDate"));
        Assert.True(sm.CanTransition("AwaitingEffectiveDate", "FinalSettlement"));
        Assert.True(sm.CanTransition("FinalSettlement", "Completed"));
    }

    [Fact]
    public void Completed_IsTerminal()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        Assert.True(sm.IsTerminal("Completed"));
    }

    [Fact]
    public void Rejected_IsTerminal()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        Assert.True(sm.IsTerminal("Rejected"));
    }

    [Fact]
    public void Active_IsNotTerminal()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        Assert.False(sm.IsTerminal("Active"));
    }

    [Fact]
    public void GetValidTransitions_FromCreated_ReturnsSubmittedAndCancelled()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        var transitions = sm.GetValidTransitions("Created");

        Assert.Contains("Submitted", transitions);
        Assert.Contains("Cancelled", transitions);
        Assert.Equal(2, transitions.Count);
    }

    [Fact]
    public void GetValidTransitions_FromTerminalState_ReturnsEmpty()
    {
        var sm = new Brs001StateMachine(ProcessRole.Initiator);
        var transitions = sm.GetValidTransitions("Completed");
        Assert.Empty(transitions);
    }
}
