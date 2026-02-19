using System.Text.Json;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs010HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571313180000000005");
    private static readonly GlnNumber SupplierGln = GlnNumber.Create("5790000000005");
    private static readonly DateTimeOffset EffectiveDate = DateTimeOffset.UtcNow.AddDays(5);

    private static Supply CreateActiveSupply() =>
        Supply.Create(Guid.NewGuid(), Guid.NewGuid(),
            Period.From(DateTimeOffset.UtcNow.AddYears(-1)));

    [Fact]
    public void ExecuteMoveOut_CreatesProcess()
    {
        var supply = CreateActiveSupply();

        var result = Brs010Handler.ExecuteMoveOut(TestGsrn, EffectiveDate, supply, SupplierGln);

        Assert.Equal(ProcessType.Fraflytning, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Initiator, result.Process.Role);
        Assert.Equal(EffectiveDate, result.Process.EffectiveDate);
    }

    [Fact]
    public void ExecuteMoveOut_EndsSupply()
    {
        var supply = CreateActiveSupply();

        var result = Brs010Handler.ExecuteMoveOut(TestGsrn, EffectiveDate, supply, SupplierGln);

        Assert.Equal(EffectiveDate, result.EndedSupply.SupplyPeriod.End);
        Assert.False(result.EndedSupply.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void ExecuteMoveOut_CreatesOutboxMessage()
    {
        var supply = CreateActiveSupply();

        var result = Brs010Handler.ExecuteMoveOut(TestGsrn, EffectiveDate, supply, SupplierGln);

        Assert.Equal("RSM-005", result.OutboxMessage.DocumentType);
        Assert.Equal("BRS-010", result.OutboxMessage.BusinessProcess);
        Assert.Equal(SupplierGln.Value, result.OutboxMessage.SenderGln);
        Assert.Equal("5790001330552", result.OutboxMessage.ReceiverGln);
        Assert.Equal(result.Process.Id, result.OutboxMessage.ProcessId);
        Assert.False(result.OutboxMessage.IsSent);
    }

    [Fact]
    public void ExecuteMoveOut_ThrowsIfSupplyNotActive()
    {
        var supply = Supply.Create(Guid.NewGuid(), Guid.NewGuid(),
            Period.Create(DateTimeOffset.UtcNow.AddYears(-2), DateTimeOffset.UtcNow.AddYears(-1)));

        Assert.Throws<InvalidOperationException>(() =>
            Brs010Handler.ExecuteMoveOut(TestGsrn, EffectiveDate, supply, SupplierGln));
    }

    [Fact]
    public void ExecuteMoveOut_OutboxPayloadContainsE66()
    {
        var supply = CreateActiveSupply();

        var result = Brs010Handler.ExecuteMoveOut(TestGsrn, EffectiveDate, supply, SupplierGln);

        var payload = JsonSerializer.Deserialize<JsonElement>(result.OutboxMessage.Payload);
        Assert.Equal("E66", payload.GetProperty("businessReason").GetString());
        Assert.Equal(TestGsrn.Value, payload.GetProperty("gsrn").GetString());
    }

    [Fact]
    public void ExecuteMoveOut_ProcessIsCompleted()
    {
        var supply = CreateActiveSupply();

        var result = Brs010Handler.ExecuteMoveOut(TestGsrn, EffectiveDate, supply, SupplierGln);

        Assert.Equal("Completed", result.Process.CurrentState);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.NotNull(result.Process.CompletedAt);
        Assert.NotNull(result.Process.TransactionId);
    }
}
