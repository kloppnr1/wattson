using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs023RequestHandlerTests
{
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestAggregatedData_CreatesProcessWithCorrectTypeAndRole()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        Assert.Equal(ProcessType.AggregetDataAnmodning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void RequestAggregatedData_OutboxMessageHasCorrectDocumentType()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        Assert.Equal("RSM-016", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-023", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void RequestAggregatedData_CimPayloadHasCorrectRootElement()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        Assert.True(doc.RootElement.TryGetProperty("RequestAggregatedMeasureData_MarketDocument", out _));
    }

    [Fact]
    public void RequestAggregatedData_CimPayloadHasE74TypeCode()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestAggregatedMeasureData_MarketDocument");
        var typeCode = root.GetProperty("type").GetProperty("value").GetString();
        Assert.Equal("E74", typeCode);
    }

    [Fact]
    public void RequestAggregatedData_CimPayloadUsesDglReceiverRole()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestAggregatedMeasureData_MarketDocument");
        var receiverRole = root.GetProperty("receiver_MarketParticipant.marketRole.type")
            .GetProperty("value").GetString();
        Assert.Equal("DGL", receiverRole);
    }

    [Fact]
    public void RequestAggregatedData_GridAreaUsesNdkCodingScheme()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestAggregatedMeasureData_MarketDocument");
        var series = root.GetProperty("Series")[0];
        var gridArea = series.GetProperty("meteringGridArea_Domain.mRID");
        Assert.Equal("NDK", gridArea.GetProperty("codingScheme").GetString());
        Assert.Equal("543", gridArea.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("D04")]
    [InlineData("D05")]
    [InlineData("D32")]
    public void RequestAggregatedData_DifferentProcessTypes_IncludedInPayload(string processType)
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1, processType: processType);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestAggregatedMeasureData_MarketDocument");
        var pt = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal(processType, pt);
    }

    [Fact]
    public void RequestAggregatedData_InvalidDateRange_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs023RequestHandler.RequestAggregatedData(
                TestGln, "543", Feb1, Jan1));
    }

    [Fact]
    public void RequestAggregatedData_EqualDates_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs023RequestHandler.RequestAggregatedData(
                TestGln, "543", Jan1, Jan1));
    }

    [Fact]
    public void RequestAggregatedData_EmptyGridArea_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs023RequestHandler.RequestAggregatedData(
                TestGln, "", Jan1, Feb1));
    }

    [Fact]
    public void RequestAggregatedData_OutboxSentToDataHub()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }

    [Fact]
    public void RequestAggregatedData_MeteringPointTypeInPayload()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1, meteringPointType: "E18");

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestAggregatedMeasureData_MarketDocument");
        var series = root.GetProperty("Series")[0];
        var mpType = series.GetProperty("marketEvaluationPoint.type")
            .GetProperty("value").GetString();
        Assert.Equal("E18", mpType);
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        Brs023RequestHandler.HandleRejection(result.Process, "E86: No data");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("E86", result.Process.ErrorMessage);
    }

    [Fact]
    public void HandleDataReceived_CompletesProcess()
    {
        var result = Brs023RequestHandler.RequestAggregatedData(
            TestGln, "543", Jan1, Feb1);

        Brs023RequestHandler.HandleDataReceived(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("DataReceived", result.Process.CurrentState);
    }
}
