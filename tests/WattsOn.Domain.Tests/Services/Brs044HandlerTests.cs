using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs044HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571234567890123456");
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000610976");
    private static readonly DateTimeOffset EffectiveDate = DateTimeOffset.UtcNow.AddDays(1);
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static MeteringPoint CreateMp()
    {
        var mp = MeteringPoint.Create(
            TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GridGln);
        return mp;
    }

    private static Supply CreateActiveSupply(Guid meteringPointId)
    {
        var period = Period.From(DateTimeOffset.UtcNow.AddDays(-30));
        return Supply.Create(meteringPointId, CustomerId, period);
    }

    [Fact]
    public void IncomingTransfer_CreatesSupply()
    {
        var mp = CreateMp();
        var data = new Brs044Handler.IncomingTransferData(TestGsrn, EffectiveDate, CustomerId, mp.Id);

        var result = Brs044Handler.HandleIncomingTransfer(data);

        Assert.NotNull(result.NewSupply);
        Assert.Equal(mp.Id, result.NewSupply!.MeteringPointId);
        Assert.Equal(CustomerId, result.NewSupply.CustomerId);
        Assert.Equal(EffectiveDate, result.NewSupply.SupplyPeriod.Start);
        Assert.True(result.NewSupply.SupplyPeriod.IsOpenEnded);
    }

    [Fact]
    public void IncomingTransfer_LinksCustomer()
    {
        var mp = CreateMp();
        var data = new Brs044Handler.IncomingTransferData(TestGsrn, EffectiveDate, CustomerId, mp.Id);

        var result = Brs044Handler.HandleIncomingTransfer(data);

        Assert.Equal(CustomerId, result.NewSupply!.CustomerId);
        Assert.Equal(result.Process.Id, result.NewSupply.CreatedByProcessId);
    }

    [Fact]
    public void OutgoingTransfer_EndsActiveSupply()
    {
        var mp = CreateMp();
        mp.SetActiveSupply(true);
        var supply = CreateActiveSupply(mp.Id);
        var data = new Brs044Handler.OutgoingTransferData(TestGsrn, EffectiveDate);

        var result = Brs044Handler.HandleOutgoingTransfer(data, mp, supply);

        Assert.NotNull(result.EndedSupply);
        Assert.Equal(EffectiveDate, result.EndedSupply!.SupplyPeriod.End);
        Assert.False(mp.HasActiveSupply);
    }

    [Fact]
    public void Transfer_CreatesProcess_WithCorrectType()
    {
        var mp = CreateMp();
        var inData = new Brs044Handler.IncomingTransferData(TestGsrn, EffectiveDate, CustomerId, mp.Id);
        var outData = new Brs044Handler.OutgoingTransferData(TestGsrn, EffectiveDate);

        var inResult = Brs044Handler.HandleIncomingTransfer(inData);
        var outResult = Brs044Handler.HandleOutgoingTransfer(outData, mp, null);

        Assert.Equal(ProcessType.TvungetLeverandørskift, inResult.Process.ProcessType);
        Assert.Equal(ProcessType.TvungetLeverandørskift, outResult.Process.ProcessType);
        Assert.Equal(ProcessRole.Recipient, inResult.Process.Role);
        Assert.Equal(ProcessStatus.Completed, inResult.Process.Status);
    }

    [Fact]
    public void OutgoingTransfer_HandlesNoActiveSupply_Gracefully()
    {
        var mp = CreateMp();
        var data = new Brs044Handler.OutgoingTransferData(TestGsrn, EffectiveDate);

        var result = Brs044Handler.HandleOutgoingTransfer(data, mp, activeSupply: null);

        Assert.Null(result.EndedSupply);
        Assert.NotNull(result.Process);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
    }

    [Fact]
    public void Transfer_RecordsEffectiveDate()
    {
        var mp = CreateMp();
        var inData = new Brs044Handler.IncomingTransferData(TestGsrn, EffectiveDate, CustomerId, mp.Id);
        var outData = new Brs044Handler.OutgoingTransferData(TestGsrn, EffectiveDate);

        var inResult = Brs044Handler.HandleIncomingTransfer(inData);
        var outResult = Brs044Handler.HandleOutgoingTransfer(outData, mp, null);

        Assert.Equal(EffectiveDate, inResult.Process.EffectiveDate);
        Assert.Equal(EffectiveDate, outResult.Process.EffectiveDate);
        Assert.Equal(TestGsrn, inResult.Process.MeteringPointGsrn);
    }
}
