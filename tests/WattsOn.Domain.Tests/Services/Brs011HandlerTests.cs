using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs011HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");
    private static readonly GlnNumber OtherGln = GlnNumber.Create("5790000000128");

    // ==================== INITIATOR TESTS ====================

    [Fact]
    public void InitiateReversal_MoveIn_CreatesProcessAndOutbox()
    {
        var moveDate = DateTimeOffset.UtcNow.AddDays(-10);

        var result = Brs011Handler.InitiateReversal(TestGsrn, moveDate, TestGln, "move-in", "Fejlagtig tilflytning");

        Assert.Equal(ProcessType.FejlagtigFlytning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
        Assert.Equal("RSM-005", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-011", result.OutboxMessage.BusinessProcess);
        Assert.Contains("D33", result.OutboxMessage.Payload);
        Assert.Contains("move-in", result.OutboxMessage.Payload);
    }

    [Fact]
    public void InitiateReversal_MoveOut_CreatesProcess()
    {
        var moveDate = DateTimeOffset.UtcNow.AddDays(-5);

        var result = Brs011Handler.InitiateReversal(TestGsrn, moveDate, TestGln, "move-out", "Error");

        Assert.Contains("move-out", result.OutboxMessage.Payload);
    }

    [Fact]
    public void InitiateReversal_InvalidMoveType_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Brs011Handler.InitiateReversal(TestGsrn, DateTimeOffset.UtcNow.AddDays(-5), TestGln, "invalid", "Error"));

        Assert.Contains("moveType", ex.Message);
    }

    [Fact]
    public void InitiateReversal_MoveDateOver60Days_Throws()
    {
        var oldDate = DateTimeOffset.UtcNow.AddDays(-61);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Brs011Handler.InitiateReversal(TestGsrn, oldDate, TestGln, "move-in", "Too late"));

        Assert.Contains("60", ex.Message);
    }

    [Fact]
    public void HandleCorrectionAccepted_EndsSupply()
    {
        var moveDate = DateTimeOffset.UtcNow.AddDays(-10);
        var result = Brs011Handler.InitiateReversal(TestGsrn, moveDate, TestGln, "move-in", "Error");

        var supply = CreateActiveSupply();
        Brs011Handler.HandleCorrectionAccepted(result.Process, supply, moveDate);

        Assert.False(supply.IsActive);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
    }

    [Fact]
    public void HandleCorrectionRejected_SetsRejected()
    {
        var moveDate = DateTimeOffset.UtcNow.AddDays(-10);
        var result = Brs011Handler.InitiateReversal(TestGsrn, moveDate, TestGln, "move-in", "Error");

        Brs011Handler.HandleCorrectionRejected(result.Process, "Aftale opsagt");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
    }

    // ==================== RECIPIENT TESTS ====================

    [Fact]
    public void HandleResumeRequest_CreatesProcessAndSupply()
    {
        var mp = CreateMeteringPoint();
        var customer = CreateCustomer();
        var resumeDate = DateTimeOffset.UtcNow.AddDays(-5);

        var result = Brs011Handler.HandleResumeRequest(
            TestGsrn, resumeDate, "TXN-001", OtherGln, mp, customer);

        Assert.Equal(ProcessType.FejlagtigFlytning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Recipient, result.Process.Role);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal(mp.Id, result.NewSupply.MeteringPointId);
    }

    [Fact]
    public void HandleResumeRequest_SupplyStartsAtResumeDate()
    {
        var mp = CreateMeteringPoint();
        var customer = CreateCustomer();
        var resumeDate = DateTimeOffset.UtcNow.AddDays(-2);

        var result = Brs011Handler.HandleResumeRequest(
            TestGsrn, resumeDate, "TXN-002", OtherGln, mp, customer);

        Assert.Equal(resumeDate, result.NewSupply.SupplyPeriod.Start);
    }

    // ==================== HELPERS ====================

    private static Supply CreateActiveSupply()
    {
        var period = Period.From(DateTimeOffset.UtcNow.AddMonths(-6));
        return Supply.Create(Guid.NewGuid(), Guid.NewGuid(), period);
    }

    private static MeteringPoint CreateMeteringPoint()
    {
        return MeteringPoint.Create(
            TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GlnNumber.Create("5790000000005"));
    }

    private static Customer CreateCustomer()
    {
        return Customer.CreatePerson("Test Kunde", CprNumber.Create("0101901234"), Guid.NewGuid());
    }
}
