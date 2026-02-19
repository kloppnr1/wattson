using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs003HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");
    private static readonly GlnNumber OtherGln = GlnNumber.Create("5790000000128");

    // ==================== INITIATOR TESTS ====================

    [Fact]
    public void InitiateReversal_CreatesProcessAndOutbox()
    {
        var switchDate = DateTimeOffset.UtcNow.AddDays(-10);

        var result = Brs003Handler.InitiateReversal(TestGsrn, switchDate, TestGln, "Fejlagtigt skift");

        Assert.NotNull(result.Process);
        Assert.NotNull(result.OutboxMessage);
        Assert.Equal(ProcessType.FejlagtigtLeverandørskift, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
        Assert.Equal("RSM-001", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-003", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void InitiateReversal_PayloadContainsD07()
    {
        var switchDate = DateTimeOffset.UtcNow.AddDays(-5);
        var result = Brs003Handler.InitiateReversal(TestGsrn, switchDate, TestGln, "Error");

        Assert.Contains("D07", result.OutboxMessage.Payload);
        Assert.Contains(TestGsrn.Value, result.OutboxMessage.Payload);
    }

    [Fact]
    public void InitiateReversal_SwitchDateOver60Days_Throws()
    {
        var oldDate = DateTimeOffset.UtcNow.AddDays(-61);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Brs003Handler.InitiateReversal(TestGsrn, oldDate, TestGln, "Too late"));

        Assert.Contains("60", ex.Message);
    }

    [Fact]
    public void HandleCorrectionAccepted_EndsSupplyAndCompletes()
    {
        var switchDate = DateTimeOffset.UtcNow.AddDays(-10);
        var result = Brs003Handler.InitiateReversal(TestGsrn, switchDate, TestGln, "Error");

        var supply = CreateActiveSupply();
        var ended = Brs003Handler.HandleCorrectionAccepted(result.Process, supply, switchDate);

        Assert.NotNull(ended);
        Assert.False(supply.IsActive);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
    }

    [Fact]
    public void HandleCorrectionAccepted_NoSupply_StillCompletes()
    {
        var switchDate = DateTimeOffset.UtcNow.AddDays(-10);
        var result = Brs003Handler.InitiateReversal(TestGsrn, switchDate, TestGln, "Error");

        var ended = Brs003Handler.HandleCorrectionAccepted(result.Process, null, switchDate);

        Assert.Null(ended);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
    }

    [Fact]
    public void HandleCorrectionRejected_SetsRejectedStatus()
    {
        var switchDate = DateTimeOffset.UtcNow.AddDays(-10);
        var result = Brs003Handler.InitiateReversal(TestGsrn, switchDate, TestGln, "Error");

        Brs003Handler.HandleCorrectionRejected(result.Process, "Aftale opsagt");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("Aftale opsagt", result.Process.ErrorMessage);
    }

    // ==================== RECIPIENT TESTS ====================

    [Fact]
    public void HandleResumeRequest_CreatesProcessAndSupply()
    {
        var mp = CreateMeteringPoint();
        var customer = CreateCustomer();
        var resumeDate = DateTimeOffset.UtcNow.AddDays(-5);

        var result = Brs003Handler.HandleResumeRequest(
            TestGsrn, resumeDate, "TXN-001", OtherGln, mp, customer);

        Assert.NotNull(result.Process);
        Assert.NotNull(result.NewSupply);
        Assert.Equal(ProcessType.FejlagtigtLeverandørskift, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Recipient, result.Process.Role);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal(mp.Id, result.NewSupply.MeteringPointId);
        Assert.Equal(customer.Id, result.NewSupply.CustomerId);
    }

    [Fact]
    public void HandleResumeRequest_SupplyStartsAtResumeDate()
    {
        var mp = CreateMeteringPoint();
        var customer = CreateCustomer();
        var resumeDate = DateTimeOffset.UtcNow.AddDays(-3);

        var result = Brs003Handler.HandleResumeRequest(
            TestGsrn, resumeDate, "TXN-002", OtherGln, mp, customer);

        Assert.Equal(resumeDate, result.NewSupply.SupplyPeriod.Start);
        Assert.Null(result.NewSupply.SupplyPeriod.End);
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
