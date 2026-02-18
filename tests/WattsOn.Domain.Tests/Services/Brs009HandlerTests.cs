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

    private static Målepunkt CreateMp() =>
        Målepunkt.Create(TestGsrn, MålepunktsType.Forbrug, MålepunktsArt.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GridGln);

    private static Kunde CreateKunde(string name = "Test Kunde") =>
        Kunde.CreatePerson(name, CprNumber.Create("0101901234"));

    // --- Move-In ---

    [Fact]
    public void MoveIn_CreatesProcessAndLeverance()
    {
        var mp = CreateMp();
        var kunde = CreateKunde("Anna Larsen");
        var aktørId = Guid.NewGuid();

        var result = Brs009Handler.ExecuteMoveIn(
            TestGsrn, DateTimeOffset.UtcNow.AddDays(5), "0101901234", null,
            mp, kunde, aktørId, null);

        Assert.Equal(ProcessType.Tilflytning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(Brs009StateMachine.Completed, result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Gennemført, result.Process.Status);
        Assert.NotNull(result.NewLeverance);
        Assert.Equal(mp.Id, result.NewLeverance.MålepunktId);
        Assert.Equal(kunde.Id, result.NewLeverance.KundeId);
        Assert.Null(result.EndedLeverance);
    }

    [Fact]
    public void MoveIn_NoCprOrCvr_Throws()
    {
        var mp = CreateMp();
        var kunde = CreateKunde();

        Assert.Throws<InvalidOperationException>(() =>
            Brs009Handler.ExecuteMoveIn(
                TestGsrn, DateTimeOffset.UtcNow.AddDays(5), null, null,
                mp, kunde, Guid.NewGuid(), null));
    }

    [Fact]
    public void MoveIn_WithExistingLeverance_EndsPrevious()
    {
        var mp = CreateMp();
        var newKunde = CreateKunde("Ny Beboer");
        var effectiveDate = DateTimeOffset.UtcNow.AddDays(5);
        var oldLeverance = Leverance.Create(mp.Id, Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-2)));

        var result = Brs009Handler.ExecuteMoveIn(
            TestGsrn, effectiveDate, "0101901234", null,
            mp, newKunde, Guid.NewGuid(), oldLeverance);

        Assert.NotNull(result.EndedLeverance);
        Assert.Equal(effectiveDate, result.EndedLeverance!.SupplyPeriod.End);
        Assert.NotNull(result.NewLeverance);
        Assert.True(result.NewLeverance.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void MoveIn_HasCorrectTransitions()
    {
        var mp = CreateMp();
        var kunde = CreateKunde();

        var result = Brs009Handler.ExecuteMoveIn(
            TestGsrn, DateTimeOffset.UtcNow.AddDays(5), "0101901234", null,
            mp, kunde, Guid.NewGuid(), null);

        Assert.True(result.Process.Transitions.Count >= 3);
        Assert.Equal(Brs009StateMachine.Completed, result.Process.Transitions.Last().ToState);
        Assert.NotNull(result.Process.TransactionId);
        Assert.NotNull(result.Process.CompletedAt);
    }

    // --- Move-Out ---

    [Fact]
    public void MoveOut_EndsLeverance()
    {
        var effectiveDate = DateTimeOffset.UtcNow.AddDays(5);
        var leverance = Leverance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs009Handler.ExecuteMoveOut(TestGsrn, effectiveDate, leverance);

        Assert.Equal(ProcessType.Fraflytning, result.Process.ProcessType);
        Assert.Equal(ProcessStatus.Gennemført, result.Process.Status);
        Assert.Equal(effectiveDate, result.EndedLeverance.SupplyPeriod.End);
        Assert.False(result.EndedLeverance.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void MoveOut_InactiveLeverance_Throws()
    {
        var leverance = Leverance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Period.Create(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(-1)));

        Assert.Throws<InvalidOperationException>(() =>
            Brs009Handler.ExecuteMoveOut(TestGsrn, DateTimeOffset.UtcNow.AddDays(5), leverance));
    }

    [Fact]
    public void MoveOut_HasAuditTrail()
    {
        var leverance = Leverance.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs009Handler.ExecuteMoveOut(TestGsrn, DateTimeOffset.UtcNow.AddDays(5), leverance);

        Assert.True(result.Process.Transitions.Count >= 3);
        Assert.Equal("Completed", result.Process.CurrentState);
        Assert.NotNull(result.Process.TransactionId);
    }
}
