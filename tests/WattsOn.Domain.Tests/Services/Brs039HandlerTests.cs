using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs039HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset RequestDate = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestService_CreatesProcessWithCorrectType()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Disconnect", RequestDate);

        Assert.Equal(ProcessType.Serviceydelse, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void RequestService_OutboxMessageSentToDataHub()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Disconnect", RequestDate);

        Assert.Equal("RSM-020", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-039", result.OutboxMessage.BusinessProcess);
        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }

    [Fact]
    public void RequestService_CimPayloadContainsGsrnAndServiceType()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Disconnect", RequestDate);

        Assert.Contains(TestGsrn.Value, result.OutboxMessage.Payload);
        Assert.Contains("Disconnect", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestService_Disconnect_UsesD09ProcessType()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Disconnect", RequestDate);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestService_MarketDocument");
        var processType = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal("D09", processType);
    }

    [Fact]
    public void RequestService_Reconnect_UsesD10ProcessType()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Reconnect", RequestDate);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestService_MarketDocument");
        var processType = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal("D10", processType);
    }

    [Fact]
    public void RequestService_MeterInvestigation_UsesD11ProcessType()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "MeterInvestigation", RequestDate);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestService_MarketDocument");
        var processType = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal("D11", processType);
    }

    [Fact]
    public void RequestService_InvalidServiceType_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs039Handler.RequestService(TestGsrn, TestGln, "Invalid", RequestDate));
    }

    [Fact]
    public void HandleAcceptance_CompletesProcess()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Disconnect", RequestDate);

        Brs039Handler.HandleAcceptance(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("Completed", result.Process.CurrentState);
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Disconnect", RequestDate);

        Brs039Handler.HandleRejection(result.Process, "Service not available");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("Service not available", result.Process.ErrorMessage);
    }

    [Fact]
    public void ProcessHasGsrn()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Reconnect", RequestDate);

        Assert.Equal(TestGsrn.Value, result.Process.MeteringPointGsrn?.Value);
    }

    [Fact]
    public void RequestService_WithReason_IncludesInPayload()
    {
        var result = Brs039Handler.RequestService(TestGsrn, TestGln, "Disconnect", RequestDate, "Non-payment");

        Assert.Contains("Non-payment", result.OutboxMessage.Payload);
    }
}
