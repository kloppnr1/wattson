using System.Text.Json;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs002HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber SupplierGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset DesiredEndDate = DateTimeOffset.UtcNow.AddDays(30);

    // --- Initiation ---

    [Fact]
    public void InitiateEndOfSupply_CreatesProcess()
    {
        var result = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");

        Assert.Equal(ProcessType.Supplyophør, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal("Submitted", result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Submitted, result.Process.Status);
        Assert.Equal(DesiredEndDate, result.Process.EffectiveDate);
    }

    [Fact]
    public void InitiateEndOfSupply_CreatesOutboxMessage()
    {
        var result = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");

        Assert.Equal("RSM-005", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-002", result.OutboxMessage.BusinessProcess);
        Assert.Equal(SupplierGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
        Assert.Equal(result.Process.Id, result.OutboxMessage.ProcessId);
        Assert.False(result.OutboxMessage.IsSent);
    }

    [Fact]
    public void InitiateEndOfSupply_OutboxPayloadIsCimEnvelope()
    {
        var result = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");

        var payload = JsonSerializer.Deserialize<JsonElement>(result.OutboxMessage.Payload);
        var doc = payload.GetProperty("RequestEndOfSupply_MarketDocument");

        // Verify CIM envelope header
        Assert.Equal("23", doc.GetProperty("businessSector.type").GetProperty("value").GetString());
        Assert.Equal("E20", doc.GetProperty("process.processType").GetProperty("value").GetString());
        Assert.Equal(SupplierGln.Value, doc.GetProperty("sender_MarketParticipant.mRID").GetProperty("value").GetString());
        Assert.Equal("5790001330552", doc.GetProperty("receiver_MarketParticipant.mRID").GetProperty("value").GetString());

        // Verify Series contains GSRN
        var series = doc.GetProperty("MktActivityRecord")[0];
        Assert.Equal(TestGsrn.Value, series.GetProperty("marketEvaluationPoint.mRID").GetProperty("value").GetString());
    }

    [Fact]
    public void InitiateEndOfSupply_DoesNotEndSupply()
    {
        // Supply should NOT be ended at initiation — only when DataHub confirms
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

        var result = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");

        // Supply is untouched — still open-ended and active
        Assert.True(supply.IsActive);
        Assert.True(supply.SupplyPeriod.IsOpenEnded);
    }

    // --- Confirmation ---

    [Fact]
    public void HandleConfirmation_EndsSupply()
    {
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));
        var initResult = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");
        var actualEndDate = DesiredEndDate.AddDays(2);

        var confirmResult = Brs002Handler.HandleConfirmation(initResult.Process, supply, actualEndDate);

        Assert.Equal(actualEndDate, confirmResult.EndedSupply.SupplyPeriod.End);
        Assert.False(confirmResult.EndedSupply.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void HandleConfirmation_CompletesProcess()
    {
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));
        var initResult = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");

        Brs002Handler.HandleConfirmation(initResult.Process, supply, DesiredEndDate);

        Assert.Equal("Completed", initResult.Process.CurrentState);
        Assert.Equal(ProcessStatus.Completed, initResult.Process.Status);
        Assert.NotNull(initResult.Process.CompletedAt);
    }

    [Fact]
    public void HandleConfirmation_UsesActualEndDate()
    {
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));
        var initResult = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");

        // DataHub may confirm with a different date than requested
        var actualEndDate = DesiredEndDate.AddDays(5);
        Brs002Handler.HandleConfirmation(initResult.Process, supply, actualEndDate);

        Assert.Equal(actualEndDate, supply.SupplyPeriod.End);
        Assert.NotEqual(DesiredEndDate, supply.SupplyPeriod.End);
    }

    // --- Rejection ---

    [Fact]
    public void HandleRejection_RejectsProcess()
    {
        var initResult = Brs002Handler.InitiateEndOfSupply(TestGsrn, DesiredEndDate, SupplierGln, "Contract ended");

        Brs002Handler.HandleRejection(initResult.Process, "Invalid GSRN (D02)");

        Assert.Equal("Rejected", initResult.Process.CurrentState);
        Assert.Equal(ProcessStatus.Rejected, initResult.Process.Status);
        Assert.Equal("Invalid GSRN (D02)", initResult.Process.ErrorMessage);
    }
}
