using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs005HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");

    [Fact]
    public void RequestMasterData_CreatesProcessWithCorrectType()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        Assert.Equal(ProcessType.StamdataAnmodning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void RequestMasterData_OutboxMessageHasCorrectRsmType()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        Assert.Equal("RSM-020", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-005", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void RequestMasterData_CimPayloadContainsGsrn()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        Assert.Contains(TestGsrn.Value, result.OutboxMessage.Payload);
        Assert.Contains("E0G", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestMasterData_CimPayloadHasCorrectRootElement()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        Assert.True(doc.RootElement.TryGetProperty("RequestService_MarketDocument", out _));
    }

    [Fact]
    public void HandleRejection_SetsRejectedStatus()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        Brs005Handler.HandleRejection(result.Process, "E10: Metering point not found");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("E10", result.Process.ErrorMessage);
    }

    [Fact]
    public void HandleDataReceived_CompletesProcess()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        Brs005Handler.HandleDataReceived(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("DataReceived", result.Process.CurrentState);
    }

    [Fact]
    public void OutboxMessage_SentToDataHub()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }

    [Fact]
    public void ProcessHasGsrn()
    {
        var result = Brs005Handler.RequestMasterData(TestGsrn, TestGln);

        Assert.Equal(TestGsrn.Value, result.Process.MeteringPointGsrn?.Value);
    }
}
