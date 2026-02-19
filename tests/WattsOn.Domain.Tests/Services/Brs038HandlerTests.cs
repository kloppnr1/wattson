using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs038HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");

    [Fact]
    public void RequestChargeLinks_CreatesProcessAndOutbox()
    {
        var startDate = DateTimeOffset.UtcNow.AddMonths(-1);

        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, startDate);

        Assert.Equal(ProcessType.PristilknytningAnmodning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
        Assert.Equal("RSM-032", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-038", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void RequestChargeLinks_PayloadContainsGsrn()
    {
        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, DateTimeOffset.UtcNow);

        Assert.Contains(TestGsrn.Value, result.OutboxMessage.Payload);
        Assert.Contains("E0G", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestChargeLinks_WithEndDate_IncludesIt()
    {
        var start = DateTimeOffset.UtcNow.AddMonths(-3);
        var end = DateTimeOffset.UtcNow;

        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, start, end);

        Assert.Contains("end_DateAndOrTime.dateTime", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestChargeLinks_WithoutEndDate_OmitsEndDate()
    {
        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, DateTimeOffset.UtcNow);

        // With CIM envelope, null fields are omitted (not serialized as null)
        Assert.DoesNotContain("end_DateAndOrTime.dateTime", result.OutboxMessage.Payload);
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, DateTimeOffset.UtcNow);

        Brs038Handler.HandleRejection(result.Process, "E10: Metering point not found");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
    }

    [Fact]
    public void HandleDataReceived_CompletesProcess()
    {
        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, DateTimeOffset.UtcNow);

        Brs038Handler.HandleDataReceived(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("DataReceived", result.Process.CurrentState);
    }

    [Fact]
    public void OutboxMessage_SentToDataHub()
    {
        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, DateTimeOffset.UtcNow);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }

    [Fact]
    public void ProcessHasGsrn()
    {
        var result = Brs038Handler.RequestChargeLinks(TestGsrn, TestGln, DateTimeOffset.UtcNow);

        Assert.Equal(TestGsrn.Value, result.Process.MeteringPointGsrn?.Value);
    }
}
