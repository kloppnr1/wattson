using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes.StateMachines;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs009HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313100000000099");
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000610976");

    private static MeteringPoint CreateMp() =>
        MeteringPoint.Create(TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GridGln);

    private static Customer CreateCustomer(string name = "Test Customer") =>
        Customer.CreatePerson(name, CprNumber.Create("0101901234"));

    // --- Move-In ---

    [Fact]
    public void MoveIn_CreatesProcessAndSupply()
    {
        var mp = CreateMp();
        var customer = CreateCustomer("Anna Larsen");
        var actorId = Guid.NewGuid();

        var result = Brs009Handler.ExecuteMoveIn(
            TestGsrn, DateTimeOffset.UtcNow.AddDays(5), "0101901234", null,
            mp, customer, actorId, null);

        Assert.Equal(ProcessType.Tilflytning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(Brs009StateMachine.Completed, result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.NotNull(result.NewSupply);
        Assert.Equal(mp.Id, result.NewSupply.MeteringPointId);
        Assert.Equal(customer.Id, result.NewSupply.CustomerId);
        Assert.Null(result.EndedSupply);
    }

    [Fact]
    public void MoveIn_NoCprOrCvr_Throws()
    {
        var mp = CreateMp();
        var customer = CreateCustomer();

        Assert.Throws<InvalidOperationException>(() =>
            Brs009Handler.ExecuteMoveIn(
                TestGsrn, DateTimeOffset.UtcNow.AddDays(5), null, null,
                mp, customer, Guid.NewGuid(), null));
    }

    [Fact]
    public void MoveIn_WithExistingSupply_EndsPrevious()
    {
        var mp = CreateMp();
        var newCustomer = CreateCustomer("Ny Beboer");
        var effectiveDate = DateTimeOffset.UtcNow.AddDays(5);
        var oldSupply = Supply.Create(mp.Id, Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-2)));

        var result = Brs009Handler.ExecuteMoveIn(
            TestGsrn, effectiveDate, "0101901234", null,
            mp, newCustomer, Guid.NewGuid(), oldSupply);

        Assert.NotNull(result.EndedSupply);
        Assert.Equal(effectiveDate, result.EndedSupply!.SupplyPeriod.End);
        Assert.NotNull(result.NewSupply);
        Assert.True(result.NewSupply.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void MoveIn_HasCorrectTransitions()
    {
        var mp = CreateMp();
        var customer = CreateCustomer();

        var result = Brs009Handler.ExecuteMoveIn(
            TestGsrn, DateTimeOffset.UtcNow.AddDays(5), "0101901234", null,
            mp, customer, Guid.NewGuid(), null);

        Assert.True(result.Process.Transitions.Count >= 3);
        Assert.Equal(Brs009StateMachine.Completed, result.Process.Transitions.Last().ToState);
        Assert.NotNull(result.Process.TransactionId);
        Assert.NotNull(result.Process.CompletedAt);
    }

    // --- Move-Out ---

    [Fact]
    public void MoveOut_EndsSupply()
    {
        var effectiveDate = DateTimeOffset.UtcNow.AddDays(5);
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs009Handler.ExecuteMoveOut(TestGsrn, effectiveDate, supply);

        Assert.Equal(ProcessType.Fraflytning, result.Process.ProcessType);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal(effectiveDate, result.EndedSupply.SupplyPeriod.End);
        Assert.False(result.EndedSupply.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void MoveOut_InactiveSupply_Throws()
    {
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Period.Create(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(-1)));

        Assert.Throws<InvalidOperationException>(() =>
            Brs009Handler.ExecuteMoveOut(TestGsrn, DateTimeOffset.UtcNow.AddDays(5), supply));
    }

    [Fact]
    public void MoveOut_HasAuditTrail()
    {
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs009Handler.ExecuteMoveOut(TestGsrn, DateTimeOffset.UtcNow.AddDays(5), supply);

        Assert.True(result.Process.Transitions.Count >= 3);
        Assert.Equal("Completed", result.Process.CurrentState);
        Assert.NotNull(result.Process.TransactionId);
    }
}
