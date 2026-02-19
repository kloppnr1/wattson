using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs025HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestMeteredData_CreatesProcessWithCorrectType()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        Assert.Equal(ProcessType.MåledataAnmodning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void RequestMeteredData_CimPayloadUsesE73TypeCodeAndE23ProcessType()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestValidatedMeasureData_MarketDocument");
        var typeCode = root.GetProperty("type").GetProperty("value").GetString();
        var processType = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal("E73", typeCode);
        Assert.Equal("E23", processType);
    }

    [Fact]
    public void RequestMeteredData_CimPayloadUsesDglReceiverRole()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestValidatedMeasureData_MarketDocument");
        var receiverRole = root.GetProperty("receiver_MarketParticipant.marketRole.type")
            .GetProperty("value").GetString();
        Assert.Equal("DGL", receiverRole);
    }

    [Fact]
    public void RequestMeteredData_InvalidDateRange_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Feb1, Jan1));
    }

    [Fact]
    public void RequestMeteredData_EqualDates_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Jan1));
    }

    [Fact]
    public void RequestMeteredData_CimPayloadContainsGsrnAndDates()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        Assert.Contains(TestGsrn.Value, result.OutboxMessage.Payload);
        Assert.Contains("start_DateAndOrTime.dateTime", result.OutboxMessage.Payload);
        Assert.Contains("end_DateAndOrTime.dateTime", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestMeteredData_OutboxHasRsm015DocumentType()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        Assert.Equal("RSM-015", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-025", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        Brs025Handler.HandleRejection(result.Process, "E86: No data");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("E86", result.Process.ErrorMessage);
    }

    [Fact]
    public void HandleDataReceived_CompletesProcess()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        Brs025Handler.HandleDataReceived(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("DataReceived", result.Process.CurrentState);
    }

    [Fact]
    public void OutboxMessage_SentToDataHub()
    {
        var result = Brs025Handler.RequestMeteredData(TestGsrn, TestGln, Jan1, Feb1);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }
}
