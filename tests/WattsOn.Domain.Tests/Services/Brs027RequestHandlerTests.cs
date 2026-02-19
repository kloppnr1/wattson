using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs027RequestHandlerTests
{
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestWholesaleSettlement_CreatesProcessWithCorrectTypeAndRole()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        Assert.Equal(ProcessType.EngrosAfregningAnmodning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void RequestWholesaleSettlement_OutboxMessageHasCorrectDocumentType()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        Assert.Equal("RSM-017", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-027", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void RequestWholesaleSettlement_CimPayloadHasCorrectRootElement()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        Assert.True(doc.RootElement.TryGetProperty("RequestWholesaleSettlement_MarketDocument", out _));
    }

    [Fact]
    public void RequestWholesaleSettlement_CimPayloadHasD21TypeCode()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var typeCode = root.GetProperty("type").GetProperty("value").GetString();
        Assert.Equal("D21", typeCode);
    }

    [Fact]
    public void RequestWholesaleSettlement_CimPayloadUsesDglReceiverRole()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var receiverRole = root.GetProperty("receiver_MarketParticipant.marketRole.type")
            .GetProperty("value").GetString();
        Assert.Equal("DGL", receiverRole);
    }

    [Fact]
    public void RequestWholesaleSettlement_GridAreaUsesNdkCodingScheme()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var series = root.GetProperty("Series")[0];
        var gridArea = series.GetProperty("meteringGridArea_Domain.mRID");
        Assert.Equal("NDK", gridArea.GetProperty("codingScheme").GetString());
        Assert.Equal("543", gridArea.GetProperty("value").GetString());
    }

    [Fact]
    public void RequestWholesaleSettlement_EnergySupplierGlnInSeries()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1, energySupplierGln: "5790000000013");

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var series = root.GetProperty("Series")[0];
        var esGln = series.GetProperty("energySupplier_MarketParticipant.mRID");
        Assert.Equal("A10", esGln.GetProperty("codingScheme").GetString());
        Assert.Equal("5790000000013", esGln.GetProperty("value").GetString());
    }

    [Fact]
    public void RequestWholesaleSettlement_DefaultEnergySupplierIsSender()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var series = root.GetProperty("Series")[0];
        var esGln = series.GetProperty("energySupplier_MarketParticipant.mRID");
        Assert.Equal(TestGln.Value, esGln.GetProperty("value").GetString());
    }

    [Fact]
    public void RequestWholesaleSettlement_OptionalChargeTypeFilters()
    {
        var chargeTypes = new List<Brs027RequestHandler.ChargeTypeFilter>
        {
            new("TARIF-001", "D03"),
            new("ABONNEMENT-001", "D01"),
        };

        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1, chargeTypes: chargeTypes);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var series = root.GetProperty("Series")[0];
        var chargeTypeArray = series.GetProperty("ChargeType");
        Assert.Equal(2, chargeTypeArray.GetArrayLength());

        var first = chargeTypeArray[0];
        Assert.Equal("TARIF-001", first.GetProperty("mRID").GetString());
        Assert.Equal("D03", first.GetProperty("type").GetProperty("value").GetString());
    }

    [Fact]
    public void RequestWholesaleSettlement_WithoutChargeTypes_OmitsChargeTypeField()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var series = root.GetProperty("Series")[0];
        Assert.False(series.TryGetProperty("ChargeType", out _));
    }

    [Fact]
    public void RequestWholesaleSettlement_OutboxSentToDataHub()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }

    [Fact]
    public void RequestWholesaleSettlement_InvalidDateRange_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs027RequestHandler.RequestWholesaleSettlement(
                TestGln, "543", Feb1, Jan1));
    }

    [Fact]
    public void RequestWholesaleSettlement_EmptyGridArea_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs027RequestHandler.RequestWholesaleSettlement(
                TestGln, "", Jan1, Feb1));
    }

    [Fact]
    public void RequestWholesaleSettlement_ProcessTypeIsD05()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var pt = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal("D05", pt);
    }

    [Fact]
    public void RequestWholesaleSettlement_ChargeTypeOwnerGlnIncluded()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1, chargeTypeOwnerGln: "5790000000013");

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestWholesaleSettlement_MarketDocument");
        var series = root.GetProperty("Series")[0];
        var ownerGln = series.GetProperty("chargeTypeOwner_MarketParticipant.mRID");
        Assert.Equal("A10", ownerGln.GetProperty("codingScheme").GetString());
        Assert.Equal("5790000000013", ownerGln.GetProperty("value").GetString());
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        Brs027RequestHandler.HandleRejection(result.Process, "E47: No data");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("E47", result.Process.ErrorMessage);
    }

    [Fact]
    public void HandleDataReceived_CompletesProcess()
    {
        var result = Brs027RequestHandler.RequestWholesaleSettlement(
            TestGln, "543", Jan1, Feb1);

        Brs027RequestHandler.HandleDataReceived(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("DataReceived", result.Process.CurrentState);
    }
}
