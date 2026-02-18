using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class Brs031HandlerTests
{
    private static readonly GlnNumber OwnerGln = GlnNumber.Create("5790000610976");
    private static readonly GlnNumber AltOwnerGln = GlnNumber.Create("5790001330552");
    private static readonly DateTimeOffset Jan2026 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb2026 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar2026 = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    // --- Helper methods ---

    private static Price CreateTestPrice(
        string chargeId = "NT-001",
        PriceType type = PriceType.Tarif,
        bool isTax = false,
        bool isPassThrough = true,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null)
    {
        var period = end.HasValue
            ? Period.Create(start ?? Jan2026, end.Value)
            : Period.From(start ?? Jan2026);

        return Price.Create(chargeId, OwnerGln, type, "Nettarif C", period,
            vatExempt: false, priceResolution: Resolution.PT1H, isTax: isTax, isPassThrough: isPassThrough);
    }

    // =====================================================
    // D18 — ProcessPriceInformation (Charge Masterdata)
    // =====================================================

    [Fact]
    public void ProcessPriceInformation_NewPrice_CreatesPrice()
    {
        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "NT-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Tarif,
            description: "Nettarif C",
            effectiveDate: Jan2026,
            stopDate: null,
            resolution: Resolution.PT1H,
            vatExempt: false,
            isTax: false,
            isPassThrough: true,
            existingPrice: null);

        Assert.True(result.IsNew);
        Assert.Equal("NT-001", result.Price.ChargeId);
        Assert.Equal(OwnerGln, result.Price.OwnerGln);
        Assert.Equal(PriceType.Tarif, result.Price.Type);
        Assert.Equal("Nettarif C", result.Price.Description);
        Assert.Equal(Jan2026, result.Price.ValidityPeriod.Start);
        Assert.True(result.Price.ValidityPeriod.IsOpenEnded);
        Assert.False(result.Price.VatExempt);
        Assert.Equal(Resolution.PT1H, result.Price.PriceResolution);
        Assert.False(result.Price.IsTax);
        Assert.True(result.Price.IsPassThrough);
    }

    [Fact]
    public void ProcessPriceInformation_ExistingPrice_UpdatesPrice()
    {
        var existing = CreateTestPrice();

        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "NT-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Tarif,
            description: "Nettarif C v2",
            effectiveDate: Feb2026,
            stopDate: null,
            resolution: Resolution.PT1H,
            vatExempt: true,
            isTax: true,
            isPassThrough: false,
            existingPrice: existing);

        Assert.False(result.IsNew);
        Assert.Same(existing, result.Price);
        Assert.Equal("Nettarif C v2", result.Price.Description);
        Assert.Equal(Feb2026, result.Price.ValidityPeriod.Start);
        Assert.True(result.Price.ValidityPeriod.IsOpenEnded);
        Assert.True(result.Price.VatExempt);
        Assert.True(result.Price.IsTax);
        Assert.False(result.Price.IsPassThrough);
    }

    [Fact]
    public void ProcessPriceInformation_StopPrice_SetsEndDate()
    {
        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "NT-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Tarif,
            description: "Nettarif C",
            effectiveDate: Jan2026,
            stopDate: Feb2026,
            resolution: Resolution.PT1H,
            vatExempt: false,
            isTax: false,
            isPassThrough: true,
            existingPrice: null);

        Assert.True(result.IsNew);
        Assert.False(result.Price.ValidityPeriod.IsOpenEnded);
        Assert.Equal(Jan2026, result.Price.ValidityPeriod.Start);
        Assert.Equal(Feb2026, result.Price.ValidityPeriod.End);
    }

    [Fact]
    public void ProcessPriceInformation_UpdateExistingWithStop_ClosesValidity()
    {
        var existing = CreateTestPrice();

        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "NT-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Tarif,
            description: "Nettarif C",
            effectiveDate: Jan2026,
            stopDate: Mar2026,
            resolution: Resolution.PT1H,
            vatExempt: false,
            isTax: false,
            isPassThrough: true,
            existingPrice: existing);

        Assert.False(result.IsNew);
        Assert.Equal(Mar2026, result.Price.ValidityPeriod.End);
    }

    [Fact]
    public void ProcessPriceInformation_TaxFlagOnAbonnement_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Brs031Handler.ProcessPriceInformation(
                chargeId: "AB-001",
                ownerGln: OwnerGln,
                priceType: PriceType.Abonnement,
                description: "Abonnement",
                effectiveDate: Jan2026,
                stopDate: null,
                resolution: Resolution.P1M,
                vatExempt: false,
                isTax: true,
                isPassThrough: true,
                existingPrice: null));
    }

    [Fact]
    public void ProcessPriceInformation_TaxFlagOnGebyr_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Brs031Handler.ProcessPriceInformation(
                chargeId: "GB-001",
                ownerGln: OwnerGln,
                priceType: PriceType.Gebyr,
                description: "Gebyr",
                effectiveDate: Jan2026,
                stopDate: null,
                resolution: Resolution.PT1H,
                vatExempt: false,
                isTax: true,
                isPassThrough: false,
                existingPrice: null));
    }

    [Fact]
    public void ProcessPriceInformation_GebyrForcesNonPassThrough()
    {
        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "GB-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Gebyr,
            description: "Tilslutningsgebyr",
            effectiveDate: Jan2026,
            stopDate: null,
            resolution: Resolution.PT1H,
            vatExempt: false,
            isTax: false,
            isPassThrough: true, // Should be forced to false
            existingPrice: null);

        Assert.False(result.Price.IsPassThrough);
    }

    [Fact]
    public void ProcessPriceInformation_TaxTarif_Succeeds()
    {
        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "EA-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Tarif,
            description: "Elafgift",
            effectiveDate: Jan2026,
            stopDate: null,
            resolution: Resolution.PT1H,
            vatExempt: false,
            isTax: true,
            isPassThrough: true,
            existingPrice: null);

        Assert.True(result.Price.IsTax);
        Assert.Equal(PriceType.Tarif, result.Price.Type);
    }

    // =====================================================
    // D08 — ProcessPriceSeries (Charge Prices)
    // =====================================================

    [Fact]
    public void ProcessPriceSeries_AddsPoints()
    {
        var price = CreateTestPrice();

        var points = new List<(DateTimeOffset timestamp, decimal price)>
        {
            (Jan2026, 0.2345m),
            (Jan2026.AddHours(1), 0.2345m),
            (Jan2026.AddHours(2), 0.3456m),
        };

        var result = Brs031Handler.ProcessPriceSeries(price, Jan2026, Feb2026, points);

        Assert.Same(price, result.Price);
        Assert.Equal(3, result.PointsAdded);
        Assert.Equal(3, price.PricePoints.Count);
    }

    [Fact]
    public void ProcessPriceSeries_ReplacesExistingPoints()
    {
        var price = CreateTestPrice();

        // Add initial points
        price.AddPricePoint(Jan2026, 0.1000m);
        price.AddPricePoint(Jan2026.AddHours(1), 0.1000m);
        price.AddPricePoint(Jan2026.AddHours(2), 0.1000m);
        Assert.Equal(3, price.PricePoints.Count);

        // Replace with new points in the same range
        var newPoints = new List<(DateTimeOffset timestamp, decimal price)>
        {
            (Jan2026, 0.2345m),
            (Jan2026.AddHours(1), 0.3456m),
        };

        var result = Brs031Handler.ProcessPriceSeries(price, Jan2026, Feb2026, newPoints);

        Assert.Equal(2, result.PointsAdded);
        Assert.Equal(2, price.PricePoints.Count);
        Assert.Equal(0.2345m, price.PricePoints[0].Price);
        Assert.Equal(0.3456m, price.PricePoints[1].Price);
    }

    [Fact]
    public void ProcessPriceSeries_PartialReplace_KeepsOutsideRange()
    {
        var price = CreateTestPrice();

        // Add points across two months
        price.AddPricePoint(Jan2026, 0.1000m);
        price.AddPricePoint(Jan2026.AddHours(1), 0.1000m);
        price.AddPricePoint(Feb2026, 0.2000m);
        price.AddPricePoint(Feb2026.AddHours(1), 0.2000m);
        Assert.Equal(4, price.PricePoints.Count);

        // Replace only January points
        var newPoints = new List<(DateTimeOffset timestamp, decimal price)>
        {
            (Jan2026, 0.5000m),
        };

        var result = Brs031Handler.ProcessPriceSeries(price, Jan2026, Feb2026, newPoints);

        Assert.Equal(1, result.PointsAdded);
        // January points replaced (2 removed, 1 added) + February points kept
        Assert.Equal(3, price.PricePoints.Count);
        // February points should still be there
        Assert.Contains(price.PricePoints, pp => pp.Timestamp == Feb2026 && pp.Price == 0.2000m);
        Assert.Contains(price.PricePoints, pp => pp.Timestamp == Feb2026.AddHours(1) && pp.Price == 0.2000m);
    }

    [Fact]
    public void ProcessPriceSeries_NullPrice_Throws()
    {
        var points = new List<(DateTimeOffset timestamp, decimal price)> { (Jan2026, 0.1m) };

        Assert.Throws<ArgumentNullException>(() =>
            Brs031Handler.ProcessPriceSeries(null!, Jan2026, Feb2026, points));
    }

    [Fact]
    public void ProcessPriceSeries_EmptyPoints_ClearsRange()
    {
        var price = CreateTestPrice();
        price.AddPricePoint(Jan2026, 0.1000m);
        price.AddPricePoint(Jan2026.AddHours(1), 0.1000m);

        var result = Brs031Handler.ProcessPriceSeries(
            price, Jan2026, Feb2026, Enumerable.Empty<(DateTimeOffset, decimal)>());

        Assert.Equal(0, result.PointsAdded);
        Assert.Empty(price.PricePoints);
    }

    // =====================================================
    // D17 — ProcessPriceLinkUpdate
    // =====================================================

    [Fact]
    public void ProcessPriceLinkUpdate_NewLink_CreatesLink()
    {
        var mpId = Guid.NewGuid();
        var priceId = Guid.NewGuid();

        var result = Brs031Handler.ProcessPriceLinkUpdate(
            mpId, priceId, Jan2026, null, existingLink: null);

        Assert.True(result.IsNew);
        Assert.Equal(mpId, result.Link.MeteringPointId);
        Assert.Equal(priceId, result.Link.PriceId);
        Assert.Equal(Jan2026, result.Link.LinkPeriod.Start);
        Assert.True(result.Link.LinkPeriod.IsOpenEnded);
    }

    [Fact]
    public void ProcessPriceLinkUpdate_NewLinkWithEnd_CreatesBoundedLink()
    {
        var mpId = Guid.NewGuid();
        var priceId = Guid.NewGuid();

        var result = Brs031Handler.ProcessPriceLinkUpdate(
            mpId, priceId, Jan2026, Feb2026, existingLink: null);

        Assert.True(result.IsNew);
        Assert.False(result.Link.LinkPeriod.IsOpenEnded);
        Assert.Equal(Jan2026, result.Link.LinkPeriod.Start);
        Assert.Equal(Feb2026, result.Link.LinkPeriod.End);
    }

    [Fact]
    public void ProcessPriceLinkUpdate_ExistingLink_UpdatesPeriod()
    {
        var mpId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        var existingLink = PriceLink.Create(mpId, priceId, Period.From(Jan2026));

        var result = Brs031Handler.ProcessPriceLinkUpdate(
            mpId, priceId, Feb2026, null, existingLink);

        Assert.False(result.IsNew);
        Assert.Same(existingLink, result.Link);
        Assert.Equal(Feb2026, result.Link.LinkPeriod.Start);
        Assert.True(result.Link.LinkPeriod.IsOpenEnded);
    }

    [Fact]
    public void ProcessPriceLinkUpdate_EndLink_SetsEndDate()
    {
        var mpId = Guid.NewGuid();
        var priceId = Guid.NewGuid();
        var existingLink = PriceLink.Create(mpId, priceId, Period.From(Jan2026));

        var result = Brs031Handler.ProcessPriceLinkUpdate(
            mpId, priceId, Jan2026, Mar2026, existingLink);

        Assert.False(result.IsNew);
        Assert.False(result.Link.LinkPeriod.IsOpenEnded);
        Assert.Equal(Jan2026, result.Link.LinkPeriod.Start);
        Assert.Equal(Mar2026, result.Link.LinkPeriod.End);
    }

    // =====================================================
    // Price entity — IsTax / IsPassThrough validation
    // =====================================================

    [Fact]
    public void Price_Create_TaxOnTarif_Succeeds()
    {
        var price = Price.Create("EA-001", OwnerGln, PriceType.Tarif, "Elafgift",
            Period.From(Jan2026), isTax: true);

        Assert.True(price.IsTax);
    }

    [Fact]
    public void Price_Create_TaxOnAbonnement_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Price.Create("AB-001", OwnerGln, PriceType.Abonnement, "Abonnement",
                Period.From(Jan2026), isTax: true));
    }

    [Fact]
    public void Price_Create_TaxOnGebyr_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Price.Create("GB-001", OwnerGln, PriceType.Gebyr, "Gebyr",
                Period.From(Jan2026), isTax: true));
    }

    [Fact]
    public void Price_Create_GebyrForcesNonPassThrough()
    {
        var price = Price.Create("GB-001", OwnerGln, PriceType.Gebyr, "Tilslutningsgebyr",
            Period.From(Jan2026), isPassThrough: true);

        Assert.False(price.IsPassThrough);
    }

    [Fact]
    public void Price_UpdatePriceInfo_TaxOnNonTarif_Throws()
    {
        var price = Price.Create("AB-001", OwnerGln, PriceType.Abonnement, "Abonnement",
            Period.From(Jan2026), priceResolution: Resolution.P1M);

        Assert.Throws<InvalidOperationException>(() =>
            price.UpdatePriceInfo("Updated", isTax: true, isPassThrough: null));
    }

    [Fact]
    public void Price_UpdatePriceInfo_PassThroughOnGebyr_Throws()
    {
        var price = Price.Create("GB-001", OwnerGln, PriceType.Gebyr, "Gebyr",
            Period.From(Jan2026));

        Assert.Throws<InvalidOperationException>(() =>
            price.UpdatePriceInfo("Updated", isTax: null, isPassThrough: true));
    }

    [Fact]
    public void Price_UpdatePriceInfo_UpdatesDescription()
    {
        var price = CreateTestPrice();

        price.UpdatePriceInfo("Updated description", null, null);

        Assert.Equal("Updated description", price.Description);
        Assert.NotNull(price.UpdatedAt);
    }

    // =====================================================
    // Subscription resolution must be monthly
    // =====================================================

    [Fact]
    public void ProcessPriceInformation_Abonnement_MonthlyResolution_Succeeds()
    {
        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "AB-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Abonnement,
            description: "Abonnement",
            effectiveDate: Jan2026,
            stopDate: null,
            resolution: Resolution.P1M,
            vatExempt: false,
            isTax: false,
            isPassThrough: true,
            existingPrice: null);

        Assert.True(result.IsNew);
        Assert.Equal(Resolution.P1M, result.Price.PriceResolution);
    }

    [Fact]
    public void ProcessPriceInformation_Tarif_HourlyResolution_Succeeds()
    {
        var result = Brs031Handler.ProcessPriceInformation(
            chargeId: "NT-001",
            ownerGln: OwnerGln,
            priceType: PriceType.Tarif,
            description: "Nettarif C",
            effectiveDate: Jan2026,
            stopDate: null,
            resolution: Resolution.PT1H,
            vatExempt: false,
            isTax: false,
            isPassThrough: true,
            existingPrice: null);

        Assert.True(result.IsNew);
        Assert.Equal(Resolution.PT1H, result.Price.PriceResolution);
    }

    // =====================================================
    // ReplacePricePoints entity method
    // =====================================================

    [Fact]
    public void Price_ReplacePricePoints_ReturnsCorrectCount()
    {
        var price = CreateTestPrice();
        price.AddPricePoint(Jan2026, 0.1m);

        var count = price.ReplacePricePoints(Jan2026, Feb2026, new[]
        {
            (Jan2026, 0.2m),
            (Jan2026.AddHours(1), 0.3m),
            (Jan2026.AddHours(2), 0.4m),
        });

        Assert.Equal(3, count);
        Assert.Equal(3, price.PricePoints.Count);
    }
}
