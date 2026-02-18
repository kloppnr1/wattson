using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes;
using WattsOn.Domain.Processes.StateMachines;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs001HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313100000000099");
    private static readonly GlnNumber CounterpartGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset FutureDate = DateTimeOffset.UtcNow.AddDays(30);

    // --- Initiation ---

    [Fact]
    public void InitiateSupplierChange_CreatesProcess()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);

        Assert.Equal(ProcessType.Leverandørskift, process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, process.Role);
        Assert.Equal(Brs001StateMachine.Created, process.CurrentState);
        Assert.Equal(ProcessStatus.Oprettet, process.Status);
        Assert.Equal(FutureDate, process.EffectiveDate);
    }

    [Fact]
    public void InitiateSupplierChange_NoCprOrCvr_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Brs001Handler.InitiateSupplierChange(TestGsrn, FutureDate, null, null, CounterpartGln));
    }

    [Fact]
    public void InitiateSupplierChange_WithCvr_Succeeds()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, null, "12345678", CounterpartGln);

        Assert.Equal(ProcessType.Leverandørskift, process.ProcessType);
    }

    // --- Confirmation ---

    [Fact]
    public void HandleConfirmation_TransitionsToConfirmed()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);

        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        Assert.Equal(Brs001StateMachine.Confirmed, process.CurrentState);
        Assert.Equal(ProcessStatus.Bekræftet, process.Status);
        Assert.Equal("DH-TX-001", process.TransactionId);
    }

    // --- Rejection ---

    [Fact]
    public void HandleRejection_TransitionsToRejected()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        process.TransitionTo(Brs001StateMachine.Submitted, "Submitted");

        Brs001Handler.HandleRejection(process, "CPR mismatch (D17)");

        Assert.Equal(Brs001StateMachine.Rejected, process.CurrentState);
        Assert.Equal(ProcessStatus.Afvist, process.Status);
        Assert.Equal("CPR mismatch (D17)", process.ErrorMessage);
    }

    // --- Execute supplier change ---

    [Fact]
    public void ExecuteSupplierChange_CreatesNewLeverance()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mp = Målepunkt.Create(TestGsrn, MålepunktsType.Forbrug, MålepunktsArt.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var kunde = Kunde.CreatePerson("Test Kunde", CprNumber.Create("0101901234"));
        var ownAktørId = Guid.NewGuid();

        var result = Brs001Handler.ExecuteSupplierChange(process, mp, kunde, ownAktørId, null);

        Assert.NotNull(result.NewLeverance);
        Assert.Equal(mp.Id, result.NewLeverance!.MålepunktId);
        Assert.Equal(kunde.Id, result.NewLeverance.KundeId);
        Assert.Equal(FutureDate, result.NewLeverance.SupplyPeriod.Start);
        Assert.True(result.NewLeverance.SupplyPeriod.IsOpenEnded);
        Assert.Null(result.EndedLeverance);
    }

    [Fact]
    public void ExecuteSupplierChange_EndsExistingLeverance()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mpId = Guid.NewGuid();
        var mp = Målepunkt.Create(TestGsrn, MålepunktsType.Forbrug, MålepunktsArt.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var kunde = Kunde.CreatePerson("Test Kunde", CprNumber.Create("0101901234"));
        var oldLeverance = Leverance.Create(mp.Id, Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));
        var ownAktørId = Guid.NewGuid();

        var result = Brs001Handler.ExecuteSupplierChange(process, mp, kunde, ownAktørId, oldLeverance);

        Assert.NotNull(result.EndedLeverance);
        Assert.False(result.EndedLeverance!.SupplyPeriod.IsOpenEnded);
        Assert.Equal(FutureDate, result.EndedLeverance.SupplyPeriod.End);
    }

    [Fact]
    public void ExecuteSupplierChange_ProcessCompletes()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mp = Målepunkt.Create(TestGsrn, MålepunktsType.Forbrug, MålepunktsArt.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var kunde = Kunde.CreatePerson("Test Kunde", CprNumber.Create("0101901234"));

        Brs001Handler.ExecuteSupplierChange(process, mp, kunde, Guid.NewGuid(), null);

        Assert.Equal(Brs001StateMachine.Completed, process.CurrentState);
        Assert.Equal(ProcessStatus.Gennemført, process.Status);
        Assert.NotNull(process.CompletedAt);
    }

    // --- Recipient flow ---

    [Fact]
    public void HandleAsRecipient_EndsLeveranceAndCompletes()
    {
        var mpId = Guid.NewGuid();
        var oldLeverance = Leverance.Create(mpId, Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs001Handler.HandleAsRecipient(
            TestGsrn, FutureDate, "DH-TX-002",
            GlnNumber.Create("5790000000012"), oldLeverance);

        Assert.Equal(ProcessRole.Recipient, result.Process.Role);
        Assert.Equal(Brs001StateMachine.Completed, result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Gennemført, result.Process.Status);
        Assert.Null(result.NewLeverance);
        Assert.NotNull(result.EndedLeverance);
        Assert.Equal(FutureDate, result.EndedLeverance!.SupplyPeriod.End);
    }

    // --- Full audit trail ---

    [Fact]
    public void FullInitiatorFlow_HasCorrectTransitions()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mp = Målepunkt.Create(TestGsrn, MålepunktsType.Forbrug, MålepunktsArt.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var kunde = Kunde.CreatePerson("Test Kunde", CprNumber.Create("0101901234"));
        Brs001Handler.ExecuteSupplierChange(process, mp, kunde, Guid.NewGuid(), null);

        // Should have: Initial→Created, Created→Submitted, Submitted→Confirmed, Confirmed→Active, Active→Completed
        Assert.True(process.Transitions.Count >= 4);
        Assert.Equal(Brs001StateMachine.Completed, process.Transitions.Last().ToState);
    }
}
