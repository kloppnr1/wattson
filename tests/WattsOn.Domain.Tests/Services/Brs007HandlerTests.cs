using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs007HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571234567890123456");
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000610976");
    private static readonly DateTimeOffset EffectiveDate = DateTimeOffset.UtcNow.AddDays(1);
    private static readonly Guid SupplierId = Guid.NewGuid();

    private static MeteringPoint CreateMp(ConnectionState state = ConnectionState.Tilsluttet)
    {
        var mp = MeteringPoint.Create(
            TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GridGln);
        if (state != ConnectionState.Tilsluttet)
            mp.UpdateConnectionState(state);
        return mp;
    }

    private static Supply CreateActiveSupply(Guid meteringPointId)
    {
        var customerId = Guid.NewGuid();
        var period = Period.From(DateTimeOffset.UtcNow.AddDays(-30));
        return Supply.Create(meteringPointId, customerId, period);
    }

    [Fact]
    public void Decommission_EndsActiveSupply_AtEffectiveDate()
    {
        var mp = CreateMp();
        mp.SetActiveSupply(true);
        var supply = CreateActiveSupply(mp.Id);
        var data = new Brs007Handler.DecommissionData(TestGsrn, EffectiveDate);

        var result = Brs007Handler.Decommission(mp, data, supply);

        Assert.NotNull(result.EndedSupply);
        Assert.Equal(EffectiveDate, result.EndedSupply!.SupplyPeriod.End);
        Assert.False(mp.HasActiveSupply);
    }

    [Fact]
    public void Decommission_MarksMpAsNedlagt()
    {
        var mp = CreateMp();
        var data = new Brs007Handler.DecommissionData(TestGsrn, EffectiveDate);

        var result = Brs007Handler.Decommission(mp, data);

        Assert.Equal(ConnectionState.Nedlagt, mp.ConnectionState);
        Assert.Equal(ConnectionState.Tilsluttet, result.PreviousState);
    }

    [Fact]
    public void Decommission_NoActiveSupply_StillMarksNedlagt()
    {
        var mp = CreateMp();
        var data = new Brs007Handler.DecommissionData(TestGsrn, EffectiveDate);

        var result = Brs007Handler.Decommission(mp, data, activeSupply: null);

        Assert.Equal(ConnectionState.Nedlagt, mp.ConnectionState);
        Assert.Null(result.EndedSupply);
    }

    [Fact]
    public void Decommission_CreatesAuditProcess()
    {
        var mp = CreateMp();
        var data = new Brs007Handler.DecommissionData(TestGsrn, EffectiveDate, "Grid restructuring");

        var result = Brs007Handler.Decommission(mp, data);

        Assert.NotNull(result.Process);
        Assert.Equal(ProcessType.MålepunktNedlæggelse, result.Process.ProcessType);
        Assert.Equal(ProcessRole.Recipient, result.Process.Role);
        Assert.Equal(ProcessStatus.Completed, result.Process.Status);
        Assert.Equal(TestGsrn, result.Process.MeteringPointGsrn);
        Assert.Equal(EffectiveDate, result.Process.EffectiveDate);
    }

    [Fact]
    public void Decommission_ReturnsDecommissionResult_WithDetails()
    {
        var mp = CreateMp();
        mp.SetActiveSupply(true);
        var supply = CreateActiveSupply(mp.Id);
        var data = new Brs007Handler.DecommissionData(TestGsrn, EffectiveDate, "Permanent closedown");

        var result = Brs007Handler.Decommission(mp, data, supply);

        Assert.Equal(mp, result.MeteringPoint);
        Assert.Equal(ConnectionState.Tilsluttet, result.PreviousState);
        Assert.NotNull(result.EndedSupply);
        Assert.NotNull(result.Process);
        Assert.Equal(ConnectionState.Nedlagt, result.MeteringPoint.ConnectionState);
    }
}
