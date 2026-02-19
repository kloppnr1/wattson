using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs041HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset EffectiveDate = new(2026, 4, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestElectricalHeatingChange_CreatesProcessWithCorrectType()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Assert.Equal(ProcessType.Elvarme, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void RequestElectricalHeatingChange_CimPayloadUsesD20ProcessType()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestChangeCustomerCharacteristics_MarketDocument");
        var processType = root.GetProperty("process.processType").GetProperty("value").GetString();
        Assert.Equal("D20", processType);
    }

    [Fact]
    public void RequestElectricalHeatingChange_AddAction_CimContainsAdd()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Assert.Contains("\"add\"", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestElectricalHeatingChange_RemoveAction_CimContainsRemove()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Remove", EffectiveDate);

        Assert.Contains("\"remove\"", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestElectricalHeatingChange_InvalidAction_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Invalid", EffectiveDate));
    }

    [Fact]
    public void RequestElectricalHeatingChange_UsesRsm027DocumentType()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Assert.Equal("RSM-027", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-041", result.OutboxMessage.BusinessProcess);
    }

    [Fact]
    public void RequestElectricalHeatingChange_CimPayloadHasD15TypeCode()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        var doc = JsonDocument.Parse(result.OutboxMessage.Payload);
        var root = doc.RootElement.GetProperty("RequestChangeCustomerCharacteristics_MarketDocument");
        var typeCode = root.GetProperty("type").GetProperty("value").GetString();
        Assert.Equal("D15", typeCode);
    }

    [Fact]
    public void HandleAcceptance_CompletesProcess()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Brs041Handler.HandleAcceptance(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("Completed", result.Process.CurrentState);
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Brs041Handler.HandleRejection(result.Process, "Not allowed for production MP");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("Not allowed", result.Process.ErrorMessage);
    }

    [Fact]
    public void OutboxMessage_SentToDataHub()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }

    [Fact]
    public void ProcessHasGsrn()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Assert.Equal(TestGsrn.Value, result.Process.MeteringPointGsrn?.Value);
    }

    [Fact]
    public void RequestElectricalHeatingChange_CimPayloadContainsGsrn()
    {
        var result = Brs041Handler.RequestElectricalHeatingChange(TestGsrn, TestGln, "Add", EffectiveDate);

        Assert.Contains(TestGsrn.Value, result.OutboxMessage.Payload);
    }
}
