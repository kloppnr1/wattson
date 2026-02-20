using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class SettlementValidatorTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000432752");
    private static readonly GlnNumber TsoGln = GlnNumber.Create("5790000432752");
    private static readonly GlnNumber SupplierGln = GlnNumber.Create("5790001330552");

    private static PriceWithPoints CreatePrice(string chargeId, PriceType type, string description,
        PriceCategory category, decimal priceValue = 0.10m, Resolution? resolution = null,
        GlnNumber? ownerGln = null, bool isTax = false)
    {
        var price = Price.Create(
            chargeId, ownerGln ?? TsoGln, type, description,
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            priceResolution: resolution ?? Resolution.PT1H,
            isTax: isTax,
            category: category);
        price.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), priceValue);
        return new PriceWithPoints(price);
    }

    private static IReadOnlyList<PriceWithPoints> AllRequiredPrices() =>
    [
        CreatePrice("SPOT-DK1", PriceType.Tarif, "Spotpris — DK1", PriceCategory.SpotPris, 0.65m, Resolution.PT15M),
        CreatePrice("NET-T-DK1", PriceType.Tarif, "Nettarif — DK1", PriceCategory.Nettarif, 0.25m, ownerGln: GridGln),
        CreatePrice("SYS-T-01", PriceType.Tarif, "Systemtarif", PriceCategory.Systemtarif, 0.054m),
        CreatePrice("TRANS-T-01", PriceType.Tarif, "Transmissionstarif", PriceCategory.Transmissionstarif, 0.049m),
        CreatePrice("ELAFG-01", PriceType.Tarif, "Elafgift", PriceCategory.Elafgift, 0.761m, isTax: true),
        CreatePrice("BAL-T-01", PriceType.Tarif, "Balancetarif", PriceCategory.Balancetarif, 0.00229m),
        CreatePrice("MARGIN-01", PriceType.Tarif, "Leverandørtillæg", PriceCategory.Leverandørtillæg, 0.05m, ownerGln: SupplierGln),
    ];

    [Fact]
    public void ValidatePriceCompleteness_AllPresent_ReturnsEmpty()
    {
        var prices = AllRequiredPrices();
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Empty(missing);
    }

    [Fact]
    public void ValidatePriceCompleteness_MissingSpot_ReturnsSpotpris()
    {
        var prices = AllRequiredPrices().Where(p => p.Price.Category != PriceCategory.SpotPris).ToList();
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Single(missing);
        Assert.Contains("Spotpris", missing);
    }

    [Fact]
    public void ValidatePriceCompleteness_MissingMultiple_ReturnsAll()
    {
        var prices = AllRequiredPrices()
            .Where(p => p.Price.Category != PriceCategory.SpotPris && p.Price.Category != PriceCategory.Elafgift)
            .ToList();
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Equal(2, missing.Count);
        Assert.Contains("Spotpris", missing);
        Assert.Contains("Elafgift", missing);
    }

    [Fact]
    public void ValidatePriceCompleteness_Empty_ReturnsAllRequired()
    {
        var missing = SettlementValidator.ValidatePriceCompleteness(Array.Empty<PriceWithPoints>());
        Assert.Equal(7, missing.Count); // 6 DataHub + 1 supplier margin
    }

    [Fact]
    public void ValidatePriceCompleteness_MissingMargin_ReturnsLeverandørtillæg()
    {
        var prices = AllRequiredPrices().Where(p => p.Price.Category != PriceCategory.Leverandørtillæg).ToList();
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Single(missing);
        Assert.Contains("Leverandørtillæg", missing);
    }

    [Fact]
    public void ValidatePriceCompleteness_ProductionChargeIds_WorksByCategory()
    {
        // Prove it works with DataHub-style numeric charge IDs — no prefix matching
        var prices = new List<PriceWithPoints>
        {
            CreatePrice("8100001", PriceType.Tarif, "Spotpris DK1", PriceCategory.SpotPris, 0.65m),
            CreatePrice("40000", PriceType.Tarif, "Nettarif C", PriceCategory.Nettarif, 0.25m, ownerGln: GridGln),
            CreatePrice("40010", PriceType.Tarif, "Systemtarif", PriceCategory.Systemtarif, 0.054m),
            CreatePrice("40020", PriceType.Tarif, "Transmissionstarif", PriceCategory.Transmissionstarif, 0.049m),
            CreatePrice("EA-001", PriceType.Tarif, "Elafgift", PriceCategory.Elafgift, 0.761m, isTax: true),
            CreatePrice("40030", PriceType.Tarif, "Balancetarif", PriceCategory.Balancetarif, 0.00229m),
            CreatePrice("SUP-M-01", PriceType.Tarif, "Margin", PriceCategory.Leverandørtillæg, 0.05m, ownerGln: SupplierGln),
        };
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Empty(missing);
    }

    [Fact]
    public void ValidatePriceCompleteness_AndetCategory_NotCountedAsRequired()
    {
        // A price with Category=Andet should not satisfy any required slot
        var prices = new List<PriceWithPoints>
        {
            CreatePrice("RANDOM-01", PriceType.Tarif, "Unknown charge", PriceCategory.Andet, 0.10m),
        };
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Equal(7, missing.Count);
    }

    [Fact]
    public void ValidatePricePointCoverage_AllHavePoints_ReturnsEmpty()
    {
        var prices = AllRequiredPrices();
        var issues = SettlementValidator.ValidatePricePointCoverage(prices, Jan1, Jan1.AddMonths(1));
        Assert.Empty(issues);
    }

    [Fact]
    public void ValidatePricePointCoverage_TariffWithNoPoints_ReturnsIssue()
    {
        var emptyPrice = Price.Create(
            "NET-T-DK1", GridGln, PriceType.Tarif, "Nettarif — DK1",
            Period.From(Jan1), priceResolution: Resolution.PT1H,
            category: PriceCategory.Nettarif);
        // No price points added!

        var prices = new List<PriceWithPoints> { new(emptyPrice) };
        var issues = SettlementValidator.ValidatePricePointCoverage(prices, Jan1, Jan1.AddMonths(1));
        Assert.Single(issues);
        Assert.Contains("NET-T-DK1", issues[0]);
    }
}
