using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs008HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571234567890123456");
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000610976");

    private static MeteringPoint CreateMp(ConnectionState state = ConnectionState.Ny)
    {
        var mp = MeteringPoint.Create(
            TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GridGln);
        mp.UpdateConnectionState(state);
        return mp;
    }

    [Fact]
    public void Connect_UpdatesConnectionState_ToTilsluttet()
    {
        var mp = CreateMp(ConnectionState.Ny);

        var result = Brs008Handler.Connect(mp);

        Assert.Equal(ConnectionState.Tilsluttet, mp.ConnectionState);
        Assert.True(result.WasChanged);
        Assert.Equal(ConnectionState.Ny, result.PreviousState);
    }

    [Fact]
    public void Connect_ReturnsResult_WithPreviousAndCurrentState()
    {
        var mp = CreateMp(ConnectionState.Ny);

        var result = Brs008Handler.Connect(mp);

        Assert.Equal(mp, result.MeteringPoint);
        Assert.Equal(ConnectionState.Ny, result.PreviousState);
        Assert.True(result.WasChanged);
    }

    [Fact]
    public void Connect_NoOp_IfAlreadyTilsluttet()
    {
        var mp = CreateMp(ConnectionState.Tilsluttet);

        var result = Brs008Handler.Connect(mp);

        Assert.False(result.WasChanged);
        Assert.Equal(ConnectionState.Tilsluttet, mp.ConnectionState);
        Assert.Equal(ConnectionState.Tilsluttet, result.PreviousState);
    }
}
