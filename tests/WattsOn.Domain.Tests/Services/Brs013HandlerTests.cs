using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs013HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571234567890123456");
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000610976");

    private static MeteringPoint CreateMp(ConnectionState state = ConnectionState.Tilsluttet)
    {
        var mp = MeteringPoint.Create(
            TestGsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, "DK1", GridGln);
        if (state != ConnectionState.Tilsluttet)
            mp.UpdateConnectionState(state);
        return mp;
    }

    [Fact]
    public void Disconnect_SetsAfbrudt()
    {
        var mp = CreateMp(ConnectionState.Tilsluttet);

        var result = Brs013Handler.UpdateConnectionState(mp, ConnectionState.Afbrudt);

        Assert.Equal(ConnectionState.Afbrudt, mp.ConnectionState);
        Assert.True(result.WasChanged);
        Assert.Equal(ConnectionState.Tilsluttet, result.PreviousState);
        Assert.Equal(ConnectionState.Afbrudt, result.NewState);
    }

    [Fact]
    public void Reconnect_SetsTilsluttet()
    {
        var mp = CreateMp(ConnectionState.Afbrudt);

        var result = Brs013Handler.UpdateConnectionState(mp, ConnectionState.Tilsluttet);

        Assert.Equal(ConnectionState.Tilsluttet, mp.ConnectionState);
        Assert.True(result.WasChanged);
        Assert.Equal(ConnectionState.Afbrudt, result.PreviousState);
        Assert.Equal(ConnectionState.Tilsluttet, result.NewState);
    }

    [Fact]
    public void ParseConnectionState_ReturnsNull_ForUnknownState()
    {
        var result = Brs013Handler.ParseConnectionState("UkenTilstand");

        Assert.Null(result);
    }

    [Fact]
    public void UpdateConnectionState_ReturnsResult_WithBeforeAndAfterStates()
    {
        var mp = CreateMp(ConnectionState.Tilsluttet);

        var result = Brs013Handler.UpdateConnectionState(mp, ConnectionState.Afbrudt);

        Assert.Equal(mp, result.MeteringPoint);
        Assert.Equal(ConnectionState.Tilsluttet, result.PreviousState);
        Assert.Equal(ConnectionState.Afbrudt, result.NewState);
        Assert.True(result.WasChanged);

        // Same state → no change
        var result2 = Brs013Handler.UpdateConnectionState(mp, ConnectionState.Afbrudt);
        Assert.False(result2.WasChanged);
    }
}
