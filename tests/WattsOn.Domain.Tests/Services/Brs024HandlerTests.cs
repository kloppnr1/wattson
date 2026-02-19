using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs024HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");

    [Fact]
    public void RequestYearlySum_CreatesProcessWithCorrectType()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        Assert.Equal(ProcessType.ÅrssumAnmodning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void RequestYearlySum_OutboxHasRsm015DocumentType()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        Assert.Equal("RSM-015", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-024", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void RequestYearlySum_CimPayloadUsesE73TypeCodeAndE30ProcessType()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestValidatedMeasureData_MarketDocument");
        var typeCode = root.GetProperty("type").GetProperty("value").GetString();
        var processType = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal("E73", typeCode);
        Assert.Equal("E30", processType);
    }

    [Fact]
    public void RequestYearlySum_CimPayloadUsesDglReceiverRole()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestValidatedMeasureData_MarketDocument");
        var receiverRole = root.GetProperty("receiver_MarketParticipant.marketRole.type")
            .GetProperty("value").GetString();
        Assert.Equal("DGL", receiverRole);
    }

    [Fact]
    public void RequestYearlySum_CimPayloadContainsGsrn()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        Assert.Contains(TestGsrn.Value, result.OutboxMessage.Payload);
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        Brs024Handler.HandleRejection(result.Process, "E86: No data available");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("E86", result.Process.ErrorMessage);
    }

    [Fact]
    public void HandleDataReceived_CompletesProcess()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        Brs024Handler.HandleDataReceived(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("DataReceived", result.Process.CurrentState);
    }

    [Fact]
    public void OutboxMessage_SentToDataHub()
    {
        var result = Brs024Handler.RequestYearlySum(TestGsrn, TestGln);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }
}
