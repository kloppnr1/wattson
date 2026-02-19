using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs004HandlerTests
{
    private static readonly Gsrn TestGsrn = Gsrn.Create("571234567890123456");
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000610976");
    private static readonly Address TestAddress = Address.Create("Testvej", "42", "1000", "København");

    private static Brs004Handler.NewMeteringPointData CreateData(
        ConnectionState connectionState = ConnectionState.Ny,
        Gsrn? parentGsrn = null,
        string gridArea = "DK1",
        Address? address = null)
    {
        return new Brs004Handler.NewMeteringPointData(
            Gsrn: TestGsrn,
            Type: MeteringPointType.Forbrug,
            Art: MeteringPointCategory.Fysisk,
            SettlementMethod: SettlementMethod.Flex,
            Resolution: Resolution.PT1H,
            GridArea: gridArea,
            GridCompanyGln: GridGln,
            ConnectionState: connectionState,
            Address: address,
            ParentGsrn: parentGsrn);
    }

    [Fact]
    public void CreateMeteringPoint_CreatesNewMp_WithAllFields()
    {
        var data = CreateData(address: TestAddress);

        var result = Brs004Handler.CreateMeteringPoint(data);

        Assert.True(result.WasCreated);
        Assert.Equal(TestGsrn, result.MeteringPoint.Gsrn);
        Assert.Equal(MeteringPointType.Forbrug, result.MeteringPoint.Type);
        Assert.Equal(MeteringPointCategory.Fysisk, result.MeteringPoint.Art);
        Assert.Equal(SettlementMethod.Flex, result.MeteringPoint.SettlementMethod);
        Assert.Equal(Resolution.PT1H, result.MeteringPoint.Resolution);
        Assert.Equal("DK1", result.MeteringPoint.GridArea);
        Assert.Equal(GridGln, result.MeteringPoint.GridCompanyGln);
        Assert.Equal(TestAddress, result.MeteringPoint.Address);
        Assert.Contains("Address", result.ChangedFields);
    }

    [Fact]
    public void UpdateExistingMp_UpdatesFields_WhenGsrnAlreadyExists()
    {
        var existingMp = MeteringPoint.Create(
            TestGsrn, MeteringPointType.Produktion, MeteringPointCategory.Virtuel,
            SettlementMethod.IkkeProfileret, Resolution.PT15M, "DK2",
            GlnNumber.Create("5790000432752"));

        var data = CreateData();

        var result = Brs004Handler.UpdateExistingMeteringPoint(existingMp, data);

        Assert.False(result.WasCreated);
        Assert.Equal(MeteringPointType.Forbrug, result.MeteringPoint.Type);
        Assert.Equal(MeteringPointCategory.Fysisk, result.MeteringPoint.Art);
        Assert.Contains("Type", result.ChangedFields);
        Assert.Contains("Art", result.ChangedFields);
    }

    [Fact]
    public void CreateMeteringPoint_SetsConnectionState_ToNyForNewMps()
    {
        var data = CreateData(connectionState: ConnectionState.Ny);

        var result = Brs004Handler.CreateMeteringPoint(data);

        Assert.Equal(ConnectionState.Ny, result.MeteringPoint.ConnectionState);
    }

    [Fact]
    public void CreateMeteringPoint_HandlesOptionalParentGsrn()
    {
        var parentGsrn = Gsrn.Create("571234567890000001");
        var dataWithParent = CreateData(parentGsrn: parentGsrn);
        var dataWithoutParent = CreateData();

        var resultWith = Brs004Handler.CreateMeteringPoint(dataWithParent);
        var resultWithout = Brs004Handler.CreateMeteringPoint(dataWithoutParent);

        // Both should create valid metering points regardless of parent GSRN
        Assert.True(resultWith.WasCreated);
        Assert.True(resultWithout.WasCreated);
        Assert.NotNull(resultWith.MeteringPoint);
        Assert.NotNull(resultWithout.MeteringPoint);
    }

    [Fact]
    public void CreateMeteringPoint_SetsGridAreaAndGridCompanyGln()
    {
        var data = CreateData(gridArea: "DK2");

        var result = Brs004Handler.CreateMeteringPoint(data);

        Assert.Equal("DK2", result.MeteringPoint.GridArea);
        Assert.Equal(GridGln, result.MeteringPoint.GridCompanyGln);
        Assert.Contains("GridArea", result.ChangedFields);
        Assert.Contains("GridCompanyGln", result.ChangedFields);
    }
}
