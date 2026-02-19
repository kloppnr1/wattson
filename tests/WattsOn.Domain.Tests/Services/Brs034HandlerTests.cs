using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs034HandlerTests
{
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790000000005");

    [Fact]
    public void RequestPriceInformation_CreatesE0GRequest()
    {
        var startDate = DateTimeOffset.UtcNow.AddMonths(-1);

        var result = Brs034Handler.RequestPriceInformation(TestGln, startDate);

        Assert.Equal(ProcessType.PrisAnmodning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
        Assert.Equal("RSM-035", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-034", result.OutboxMessage.BusinessProcess);
        Assert.Contains("E0G", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestPriceSeries_CreatesD48Request()
    {
        var startDate = DateTimeOffset.UtcNow.AddMonths(-1);

        var result = Brs034Handler.RequestPriceSeries(TestGln, startDate);

        Assert.Contains("D48", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestPriceInformation_WithFilters_IncludesInPayload()
    {
        var result = Brs034Handler.RequestPriceInformation(
            TestGln,
            DateTimeOffset.UtcNow.AddMonths(-3),
            endDate: DateTimeOffset.UtcNow,
            priceOwnerGln: "5790000000013",
            priceType: "Tarif",
            chargeId: "CHARGE-001");

        Assert.Contains("5790000000013", result.OutboxMessage.Payload);
        Assert.Contains("Tarif", result.OutboxMessage.Payload);
        Assert.Contains("CHARGE-001", result.OutboxMessage.Payload);
    }

    [Fact]
    public void RequestPriceInformation_WithoutFilters_OmitsOptionalFields()
    {
        var result = Brs034Handler.RequestPriceInformation(TestGln, DateTimeOffset.UtcNow);

        Assert.DoesNotContain("priceOwnerGln", result.OutboxMessage.Payload);
        Assert.DoesNotContain("chargeId", result.OutboxMessage.Payload);
    }

    [Fact]
    public void HandleRejection_SetsRejected()
    {
        var result = Brs034Handler.RequestPriceInformation(TestGln, DateTimeOffset.UtcNow);

        Brs034Handler.HandleRejection(result.Process, "E0H: No data available");

        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Contains("E0H", result.Process.ErrorMessage);
    }

    [Fact]
    public void HandleDataReceived_CompletesProcess()
    {
        var result = Brs034Handler.RequestPriceInformation(TestGln, DateTimeOffset.UtcNow);

        Brs034Handler.HandleDataReceived(result.Process);

        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal("DataReceived", result.Process.CurrentState);
    }

    [Fact]
    public void OutboxMessage_SentToDataHub()
    {
        var result = Brs034Handler.RequestPriceInformation(TestGln, DateTimeOffset.UtcNow);

        Assert.Equal(TestGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
    }
}
