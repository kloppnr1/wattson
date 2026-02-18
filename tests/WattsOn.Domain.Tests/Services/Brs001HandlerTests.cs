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
    private static readonly Guid SupplierId = Guid.NewGuid();

    // --- Initiation ---

    [Fact]
    public void InitiateSupplierChange_CreatesProcess()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);

        Assert.Equal(ProcessType.Leverandørskift, process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, process.Role);
        Assert.Equal(Brs001StateMachine.Created, process.CurrentState);
        Assert.Equal(ProcessStatus.Created, process.Status);
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
        Assert.Equal(ProcessStatus.Confirmed, process.Status);
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
        Assert.Equal(ProcessStatus.Rejected, process.Status);
        Assert.Equal("CPR mismatch (D17)", process.ErrorMessage);
    }

    // --- Execute supplier change ---

    [Fact]
    public void ExecuteSupplierChange_CreatesNewSupply()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mp = MeteringPoint.Create(TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var customer = Customer.CreatePerson("Test Customer", CprNumber.Create("0101901234"), SupplierId);

        var result = Brs001Handler.ExecuteSupplierChange(process, mp, customer, null);

        Assert.NotNull(result.NewSupply);
        Assert.Equal(mp.Id, result.NewSupply!.MeteringPointId);
        Assert.Equal(customer.Id, result.NewSupply.CustomerId);
        Assert.Equal(FutureDate, result.NewSupply.SupplyPeriod.Start);
        Assert.True(result.NewSupply.SupplyPeriod.IsOpenEnded);
        Assert.Null(result.EndedSupply);
    }

    [Fact]
    public void ExecuteSupplierChange_EndsExistingSupply()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mp = MeteringPoint.Create(TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var customer = Customer.CreatePerson("Test Customer", CprNumber.Create("0101901234"), SupplierId);
        var oldSupply = Supply.Create(mp.Id, Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs001Handler.ExecuteSupplierChange(process, mp, customer, oldSupply);

        Assert.NotNull(result.EndedSupply);
        Assert.False(result.EndedSupply!.SupplyPeriod.IsOpenEnded);
        Assert.Equal(FutureDate, result.EndedSupply.SupplyPeriod.End);
    }

    [Fact]
    public void ExecuteSupplierChange_ProcessCompletes()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mp = MeteringPoint.Create(TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var customer = Customer.CreatePerson("Test Customer", CprNumber.Create("0101901234"), SupplierId);

        Brs001Handler.ExecuteSupplierChange(process, mp, customer, null);

        Assert.Equal(Brs001StateMachine.Completed, process.CurrentState);
        Assert.Equal(ProcessStatus.Completed, process.Status);
        Assert.NotNull(process.CompletedAt);
    }

    // --- Recipient flow ---

    [Fact]
    public void HandleAsRecipient_EndsSupplyAndCompletes()
    {
        var mpId = Guid.NewGuid();
        var oldSupply = Supply.Create(mpId, Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs001Handler.HandleAsRecipient(
            TestGsrn, FutureDate, "DH-TX-002",
            GlnNumber.Create("5790000000012"), oldSupply);

        Assert.Equal(ProcessRole.Recipient, result.Process.Role);
        Assert.Equal(Brs001StateMachine.Completed, result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Null(result.NewSupply);
        Assert.NotNull(result.EndedSupply);
        Assert.Equal(FutureDate, result.EndedSupply!.SupplyPeriod.End);
    }

    // --- Full audit trail ---

    [Fact]
    public void FullInitiatorFlow_HasCorrectTransitions()
    {
        var process = Brs001Handler.InitiateSupplierChange(
            TestGsrn, FutureDate, "0101901234", null, CounterpartGln);
        Brs001Handler.HandleConfirmation(process, "DH-TX-001");

        var mp = MeteringPoint.Create(TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000610976"));
        var customer = Customer.CreatePerson("Test Customer", CprNumber.Create("0101901234"), SupplierId);
        Brs001Handler.ExecuteSupplierChange(process, mp, customer, null);

        // Should have: Initial→Created, Created→Submitted, Submitted→Confirmed, Confirmed→Active, Active→Completed
        Assert.True(process.Transitions.Count >= 4);
        Assert.Equal(Brs001StateMachine.Completed, process.Transitions.Last().ToState);
    }
}
