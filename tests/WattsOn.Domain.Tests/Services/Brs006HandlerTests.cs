using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs006HandlerTests
{
    private static readonly GlnNumber TestGln = GlnNumber.Create("5790001330552");
    private static readonly GlnNumber OtherGln = GlnNumber.Create("5790000432752");

    private static MeteringPoint CreateTestMeteringPoint()
    {
        return MeteringPoint.Create(
            Gsrn.Create("571234567890123456"),
            MeteringPointType.Forbrug,
            MeteringPointCategory.Fysisk,
            SettlementMethod.Flex,
            Resolution.PT1H,
            "DK1",
            TestGln,
            Address.Create("Testvej", "42", "1000", "København"));
    }

    [Fact]
    public void ApplyUpdate_UpdatesType()
    {
        var mp = CreateTestMeteringPoint();
        var update = new Brs006Handler.MasterDataUpdate(
            Type: MeteringPointType.Produktion,
            Art: null, SettlementMethod: null, Resolution: null,
            ConnectionState: null, GridArea: null, GridCompanyGln: null, Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal(MeteringPointType.Produktion, mp.Type);
        Assert.Single(result.ChangedFields);
        Assert.Contains("Type", result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_UpdatesSettlementMethod()
    {
        var mp = CreateTestMeteringPoint();
        var update = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null,
            SettlementMethod: SettlementMethod.IkkeProfileret,
            Resolution: null, ConnectionState: null,
            GridArea: null, GridCompanyGln: null, Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal(SettlementMethod.IkkeProfileret, mp.SettlementMethod);
        Assert.Single(result.ChangedFields);
        Assert.Contains("SettlementMethod", result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_UpdatesGridArea()
    {
        var mp = CreateTestMeteringPoint();
        var update = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null, SettlementMethod: null, Resolution: null,
            ConnectionState: null,
            GridArea: "DK2",
            GridCompanyGln: OtherGln,
            Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal("DK2", mp.GridArea);
        Assert.Equal(OtherGln, mp.GridCompanyGln);
        Assert.Single(result.ChangedFields);
        Assert.Contains("GridArea", result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_UpdatesConnectionState()
    {
        var mp = CreateTestMeteringPoint();
        var update = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null, SettlementMethod: null, Resolution: null,
            ConnectionState: ConnectionState.Afbrudt,
            GridArea: null, GridCompanyGln: null, Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal(ConnectionState.Afbrudt, mp.ConnectionState);
        Assert.Single(result.ChangedFields);
        Assert.Contains("ConnectionState", result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_UpdatesResolution()
    {
        var mp = CreateTestMeteringPoint();
        var update = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null, SettlementMethod: null,
            Resolution: Resolution.PT15M,
            ConnectionState: null, GridArea: null, GridCompanyGln: null, Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal(Resolution.PT15M, mp.Resolution);
        Assert.Single(result.ChangedFields);
        Assert.Contains("Resolution", result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_UpdatesAddress()
    {
        var mp = CreateTestMeteringPoint();
        var newAddress = Address.Create("Nyvej", "99", "2000", "Frederiksberg", "3", "tv");
        var update = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null, SettlementMethod: null, Resolution: null,
            ConnectionState: null, GridArea: null, GridCompanyGln: null,
            Address: newAddress);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal(newAddress, mp.Address);
        Assert.Single(result.ChangedFields);
        Assert.Contains("Address", result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_MultipleFieldsChanged()
    {
        var mp = CreateTestMeteringPoint();
        var update = new Brs006Handler.MasterDataUpdate(
            Type: MeteringPointType.Produktion,
            Art: MeteringPointCategory.Virtuel,
            SettlementMethod: SettlementMethod.IkkeProfileret,
            Resolution: Resolution.PT15M,
            ConnectionState: ConnectionState.Afbrudt,
            GridArea: null, GridCompanyGln: null, Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal(5, result.ChangedFields.Count);
        Assert.Contains("Type", result.ChangedFields);
        Assert.Contains("Art", result.ChangedFields);
        Assert.Contains("SettlementMethod", result.ChangedFields);
        Assert.Contains("Resolution", result.ChangedFields);
        Assert.Contains("ConnectionState", result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_NoChanges_WhenSameValues()
    {
        var mp = CreateTestMeteringPoint();
        var update = new Brs006Handler.MasterDataUpdate(
            Type: MeteringPointType.Forbrug,           // same as current
            Art: MeteringPointCategory.Fysisk,          // same as current
            SettlementMethod: SettlementMethod.Flex,    // same as current
            Resolution: Resolution.PT1H,                // same as current
            ConnectionState: ConnectionState.Tilsluttet, // same as current
            GridArea: null, GridCompanyGln: null, Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Empty(result.ChangedFields);
    }

    [Fact]
    public void ApplyUpdate_NullFieldsIgnored()
    {
        var mp = CreateTestMeteringPoint();
        var originalType = mp.Type;
        var originalArt = mp.Art;
        var originalSettlement = mp.SettlementMethod;

        var update = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null, SettlementMethod: null, Resolution: null,
            ConnectionState: null, GridArea: null, GridCompanyGln: null, Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Empty(result.ChangedFields);
        Assert.Equal(originalType, mp.Type);
        Assert.Equal(originalArt, mp.Art);
        Assert.Equal(originalSettlement, mp.SettlementMethod);
    }

    [Fact]
    public void ApplyUpdate_GridAreaChangesGridCompanyGln()
    {
        var mp = CreateTestMeteringPoint();

        // When GridArea changes and GridCompanyGln is provided, both update
        var update = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null, SettlementMethod: null, Resolution: null,
            ConnectionState: null,
            GridArea: "DK2",
            GridCompanyGln: OtherGln,
            Address: null);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        Assert.Equal("DK2", mp.GridArea);
        Assert.Equal(OtherGln, mp.GridCompanyGln);
        Assert.Single(result.ChangedFields);
        Assert.Contains("GridArea", result.ChangedFields);

        // When GridArea changes but GridCompanyGln is null, keep existing GLN
        var mp2 = CreateTestMeteringPoint();
        var update2 = new Brs006Handler.MasterDataUpdate(
            Type: null, Art: null, SettlementMethod: null, Resolution: null,
            ConnectionState: null,
            GridArea: "DK2",
            GridCompanyGln: null,
            Address: null);

        Brs006Handler.ApplyMasterDataUpdate(mp2, update2);

        Assert.Equal("DK2", mp2.GridArea);
        Assert.Equal(TestGln, mp2.GridCompanyGln);
    }
}
