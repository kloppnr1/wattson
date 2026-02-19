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
        decimal priceValue = 0.10m, Resolution? resolution = null, GlnNumber? ownerGln = null)
    {
        var price = Price.Create(
            chargeId, ownerGln ?? TsoGln, type, description,
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            priceResolution: resolution ?? Resolution.PT1H);
        price.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), priceValue);
        return new PriceWithPoints(price);
    }

    private static IReadOnlyList<PriceWithPoints> AllRequiredPrices() =>
    [
        CreatePrice("SPOT-DK1", PriceType.Tarif, "Spotpris — DK1", 0.65m, Resolution.PT15M),
        CreatePrice("NET-T-DK1", PriceType.Tarif, "Nettarif — DK1", 0.25m, ownerGln: GridGln),
        CreatePrice("SYS-T-01", PriceType.Tarif, "Systemtarif", 0.054m),
        CreatePrice("TRANS-T-01", PriceType.Tarif, "Transmissionstarif", 0.049m),
        CreatePrice("ELAFG-01", PriceType.Tarif, "Elafgift", 0.761m),
        CreatePrice("BAL-T-01", PriceType.Tarif, "Balancetarif", 0.00229m),
        CreatePrice("MARGIN-01", PriceType.Tarif, "Leverandørtillæg", 0.05m, ownerGln: SupplierGln),
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
        var prices = AllRequiredPrices().Where(p => !p.Price.ChargeId.StartsWith("SPOT-")).ToList();
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Single(missing);
        Assert.Contains("Spotpris", missing);
    }

    [Fact]
    public void ValidatePriceCompleteness_MissingMultiple_ReturnsAll()
    {
        var prices = AllRequiredPrices()
            .Where(p => !p.Price.ChargeId.StartsWith("SPOT-") && !p.Price.ChargeId.StartsWith("ELAFG-"))
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
        Assert.Equal(6, missing.Count);
    }

    [Fact]
    public void ValidatePriceCompleteness_SupplierMarginNotRequired()
    {
        // All mandatory elements present, no supplier margin — should still be valid
        var prices = AllRequiredPrices().Where(p => !p.Price.ChargeId.StartsWith("MARGIN-")).ToList();
        var missing = SettlementValidator.ValidatePriceCompleteness(prices);
        Assert.Empty(missing);
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
            Period.From(Jan1), priceResolution: Resolution.PT1H);
        // No price points added!

        var prices = new List<PriceWithPoints> { new(emptyPrice) };
        var issues = SettlementValidator.ValidatePricePointCoverage(prices, Jan1, Jan1.AddMonths(1));
        Assert.Single(issues);
        Assert.Contains("NET-T-DK1", issues[0]);
    }
}
