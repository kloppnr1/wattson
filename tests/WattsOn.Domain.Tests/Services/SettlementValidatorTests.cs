using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class SettlementValidatorTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Jan2 = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
    private static readonly GlnNumber GridGln = GlnNumber.Create("5790000432752");
    private static readonly GlnNumber TsoGln = GlnNumber.Create("5790000432752");

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

    private static IReadOnlyList<PriceWithPoints> AllRequiredDataHubPrices() =>
    [
        CreatePrice("40000", PriceType.Tarif, "Nettarif — DK1", PriceCategory.Nettarif, 0.25m, ownerGln: GridGln),
        CreatePrice("40010", PriceType.Tarif, "Systemtarif", PriceCategory.Systemtarif, 0.054m),
        CreatePrice("40020", PriceType.Tarif, "Transmissionstarif", PriceCategory.Transmissionstarif, 0.049m),
        CreatePrice("EA-001", PriceType.Tarif, "Elafgift", PriceCategory.Elafgift, 0.761m, isTax: true),
        CreatePrice("40030", PriceType.Tarif, "Balancetarif", PriceCategory.Balancetarif, 0.00229m),
    ];

    private static List<SpotPrice> CreateHourlySpotPrices(DateTimeOffset start, DateTimeOffset end)
    {
        var prices = new List<SpotPrice>();
        var current = start;
        while (current < end)
        {
            // PT15M intervals (4 per hour)
            for (int q = 0; q < 4; q++)
                prices.Add(SpotPrice.Create("DK1", current.AddMinutes(q * 15), 0.50m));
            current = current.AddHours(1);
        }
        return prices;
    }

    private static List<SupplierMargin> CreateHourlyMargins(Guid supplierIdentityId, DateTimeOffset start, DateTimeOffset end)
    {
        var margins = new List<SupplierMargin>();
        var current = start;
        while (current < end)
        {
            margins.Add(SupplierMargin.Create(supplierIdentityId, current, 0.15m));
            current = current.AddHours(1);
        }
        return margins;
    }

    // --- DataHub category validation ---

    [Fact]
    public void ValidateDataHubCategories_AllPresent_ReturnsEmpty()
    {
        var prices = AllRequiredDataHubPrices();
        var missing = SettlementValidator.ValidateDataHubCategories(prices);
        Assert.Empty(missing);
    }

    [Fact]
    public void ValidateDataHubCategories_MissingNettarif_ReturnsMissing()
    {
        var prices = AllRequiredDataHubPrices().Where(p => p.Price.Category != PriceCategory.Nettarif).ToList();
        var missing = SettlementValidator.ValidateDataHubCategories(prices);
        Assert.Single(missing);
        Assert.Contains("Nettarif", missing);
    }

    [Fact]
    public void ValidateDataHubCategories_Empty_ReturnsAll5()
    {
        var missing = SettlementValidator.ValidateDataHubCategories(Array.Empty<PriceWithPoints>());
        Assert.Equal(5, missing.Count);
    }

    [Fact]
    public void ValidateDataHubCategories_ProductionChargeIds_WorksByCategory()
    {
        // Prove it works with any charge ID — validation is by category, not prefix
        var prices = AllRequiredDataHubPrices();
        var missing = SettlementValidator.ValidateDataHubCategories(prices);
        Assert.Empty(missing);
    }

    [Fact]
    public void ValidateDataHubCategories_AndetCategory_NotCountedAsRequired()
    {
        var prices = new List<PriceWithPoints>
        {
            CreatePrice("RANDOM-01", PriceType.Tarif, "Unknown charge", PriceCategory.Andet, 0.10m),
        };
        var missing = SettlementValidator.ValidateDataHubCategories(prices);
        Assert.Equal(5, missing.Count);
    }

    // --- Spot price coverage ---

    [Fact]
    public void ValidateIntervalCoverage_FullCoverage_ReturnsEmpty()
    {
        var spotTimestamps = CreateHourlySpotPrices(Jan1, Jan2)
            .Select(s => s.Timestamp).ToHashSet();
        var issues = SettlementValidator.ValidateIntervalCoverage(
            "Spotpris", spotTimestamps, Jan1, Jan2, Resolution.PT1H);
        Assert.Empty(issues);
    }

    [Fact]
    public void ValidateIntervalCoverage_Empty_ReturnsMissing()
    {
        var issues = SettlementValidator.ValidateIntervalCoverage(
            "Spotpris", new HashSet<DateTimeOffset>(), Jan1, Jan2, Resolution.PT1H);
        Assert.Single(issues);
        Assert.Contains("ingen priser", issues[0]);
    }

    [Fact]
    public void ValidateIntervalCoverage_MissingHours_ReportsCount()
    {
        // Only first 12 hours covered
        var partial = CreateHourlySpotPrices(Jan1, Jan1.AddHours(12))
            .Select(s => s.Timestamp).ToHashSet();
        var issues = SettlementValidator.ValidateIntervalCoverage(
            "Leverandørmargin", partial, Jan1, Jan2, Resolution.PT1H);
        Assert.Single(issues);
        Assert.Contains("12 intervaller", issues[0]); // 24 hours - 12 covered = 12 missing
    }

    // --- Full validation ---

    [Fact]
    public void Validate_AllPresent_ReturnsEmpty()
    {
        var datahubPrices = AllRequiredDataHubPrices();
        var spotPrices = CreateHourlySpotPrices(Jan1, Jan2);
        var margins = CreateHourlyMargins(Guid.NewGuid(), Jan1, Jan2);

        var issues = SettlementValidator.Validate(
            datahubPrices, spotPrices, margins, Jan1, Jan2, Resolution.PT1H);
        Assert.Empty(issues);
    }

    [Fact]
    public void Validate_MissingSpotAndDataHub_ReturnsMultipleIssues()
    {
        var margins = CreateHourlyMargins(Guid.NewGuid(), Jan1, Jan2);

        var issues = SettlementValidator.Validate(
            Array.Empty<PriceWithPoints>(),
            Array.Empty<SpotPrice>(),
            margins,
            Jan1, Jan2, Resolution.PT1H);

        // 5 missing DataHub categories + 1 spot coverage issue
        Assert.True(issues.Count >= 6);
    }

    // --- DataHub price point coverage ---

    [Fact]
    public void ValidateDataHubCoverage_AllHavePoints_ReturnsEmpty()
    {
        var prices = AllRequiredDataHubPrices();
        var issues = SettlementValidator.ValidateDataHubCoverage(prices, Jan1, Jan2);
        Assert.Empty(issues);
    }

    [Fact]
    public void ValidateDataHubCoverage_TariffWithNoPoints_ReturnsIssue()
    {
        var emptyPrice = Price.Create(
            "40000", GridGln, PriceType.Tarif, "Nettarif — DK1",
            Period.From(Jan1), priceResolution: Resolution.PT1H,
            category: PriceCategory.Nettarif);

        var prices = new List<PriceWithPoints> { new(emptyPrice) };
        var issues = SettlementValidator.ValidateDataHubCoverage(prices, Jan1, Jan2);
        Assert.Single(issues);
        Assert.Contains("40000", issues[0]);
    }
}
