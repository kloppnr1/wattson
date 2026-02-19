using System.Text.Json;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Processes;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs015HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber SupplierGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset EffectiveDate = DateTimeOffset.UtcNow.AddDays(1);

    private static Brs015Handler.CustomerUpdateData CreateTestData(string name = "Anders Andersen") =>
        new(name, "0101901234", null, "anders@test.dk", "+4512345678", null);

    [Fact]
    public void SendCustomerUpdate_CreatesProcess()
    {
        var data = CreateTestData();

        var result = Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data);

        Assert.Equal(ProcessType.CustomerStamdataOpdatering, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal("Submitted", result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
    }

    [Fact]
    public void SendCustomerUpdate_CreatesOutboxMessage()
    {
        var data = CreateTestData();

        var result = Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data);

        Assert.Equal("RSM-027", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-015", result.OutboxMessage.BusinessProcess);
        Assert.Equal(SupplierGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
        Assert.Equal(result.Process.Id, result.OutboxMessage.ProcessId);
        Assert.False(result.OutboxMessage.IsSent);
    }

    [Fact]
    public void SendCustomerUpdate_PayloadContainsE34()
    {
        var data = CreateTestData();

        var result = Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data);

        var payload = JsonSerializer.Deserialize<JsonElement>(result.OutboxMessage.Payload);
        Assert.Equal("E34", payload.GetProperty("businessReason").GetString());
        Assert.Equal(TestGsrn.Value, payload.GetProperty("gsrn").GetString());
    }

    [Fact]
    public void SendCustomerUpdate_PayloadContainsCustomerData()
    {
        var address = Address.Create("Testvej", "42", "1000", "København");
        var data = new Brs015Handler.CustomerUpdateData(
            "Anders Andersen", "0101901234", null, "anders@test.dk", "+4512345678", address);

        var result = Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data);

        var payload = JsonSerializer.Deserialize<JsonElement>(result.OutboxMessage.Payload);
        Assert.Equal("Anders Andersen", payload.GetProperty("customerName").GetString());
        Assert.Equal("0101901234", payload.GetProperty("cpr").GetString());
        Assert.Equal("anders@test.dk", payload.GetProperty("email").GetString());
        Assert.Equal("+4512345678", payload.GetProperty("phone").GetString());

        var addr = payload.GetProperty("address");
        Assert.Equal("Testvej", addr.GetProperty("streetName").GetString());
        Assert.Equal("42", addr.GetProperty("buildingNumber").GetString());
        Assert.Equal("1000", addr.GetProperty("postCode").GetString());
        Assert.Equal("København", addr.GetProperty("cityName").GetString());
    }

    [Fact]
    public void SendCustomerUpdate_ThrowsOnEmptyName()
    {
        var data = new Brs015Handler.CustomerUpdateData("", null, null, null, null, null);

        Assert.Throws<InvalidOperationException>(() =>
            Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data));
    }

    [Fact]
    public void SendCustomerUpdate_ThrowsOnUkendt()
    {
        var data = new Brs015Handler.CustomerUpdateData("(ukendt)", null, null, null, null, null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data));
        Assert.Contains("(ukendt)", ex.Message);
    }

    // --- Confirmation ---

    [Fact]
    public void HandleConfirmation_CompletesProcess()
    {
        var data = CreateTestData();
        var result = Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data);

        Brs015Handler.HandleConfirmation(result.Process);

        Assert.Equal("Completed", result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.NotNull(result.Process.CompletedAt);
    }

    // --- Rejection ---

    [Fact]
    public void HandleRejection_RejectsProcess()
    {
        var data = CreateTestData();
        var result = Brs015Handler.SendCustomerUpdate(TestGsrn, EffectiveDate, SupplierGln, data);

        Brs015Handler.HandleRejection(result.Process, "Invalid CPR (D07)");

        Assert.Equal("Rejected", result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Rejected, result.Process.Status);
        Assert.Equal("Invalid CPR (D07)", result.Process.ErrorMessage);
    }
}
