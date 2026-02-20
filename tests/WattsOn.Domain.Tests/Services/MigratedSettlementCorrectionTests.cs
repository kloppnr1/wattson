using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

/// <summary>
/// PROOF: When a customer is migrated from Xellent to WattsOn, and DataHub later
/// sends corrected metered data, WattsOn can calculate the EXACT outstanding delta
/// per line item — spot, margin, and every individual tariff.
///
/// This is WattsOn's core value proposition: detecting and pricing corrections
/// against historical invoiced data, even when that data was imported from another system.
///
/// The test mirrors the real production flow:
/// 1. Prices are migrated from Xellent (PriceElementTable → WattsOn Price entities)
/// 2. Settlements are migrated with lines referencing those Price entities
/// 3. DataHub sends a corrected time series (version 2) for the same period
/// 4. SettlementCalculator.CalculateCorrection produces exact per-line deltas
/// 5. The outstanding = sum of all correction lines
///
/// All numbers use realistic Danish electricity tariff rates.
/// </summary>
public class MigratedSettlementCorrectionTests
{
    // February 2025: 28 days × 24 hours = 672 hours
    private static readonly DateTimeOffset Feb1 = new(2025, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar1 = new(2025, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Period February = Period.Create(Feb1, Mar1);
    private static readonly Guid MpId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private const int HoursInFeb = 672;

    // Realistic Danish tariff rates (DKK/kWh)
    private const decimal NettarifRate = 0.2616m;     // N1 nettarif C-kunde
    private const decimal SystemtarifRate = 0.054m;   // Energinet systemtarif
    private const decimal TransmissionRate = 0.049m;  // Energinet transmissionstarif
    private const decimal ElafgiftRate = 0.008m;      // Elafgift (post-2025 level)
    private const decimal SpotPriceAvg = 0.45m;       // Avg spot price DKK/kWh
    private const decimal MarginRate = 0.04m;         // Supplier margin addon

    private static Supply CreateSupply() =>
        Supply.Create(MpId, CustomerId, Period.From(new DateTimeOffset(2020, 5, 19, 0, 0, 0, TimeSpan.Zero)));

    private static TimeSeries CreateTimeSeries(decimal kwhPerHour, int version)
    {
        var ts = TimeSeries.Create(MpId, February, Resolution.PT1H, version, $"TX-{version:D3}");
        for (int i = 0; i < HoursInFeb; i++)
        {
            ts.AddObservation(
                Feb1.AddHours(i),
                EnergyQuantity.Create(kwhPerHour),
                version == 1 ? QuantityQuality.Measured : QuantityQuality.Revised);
        }
        return ts;
    }

    private static PriceWithPoints CreateTariff(string chargeId, string description, decimal rate)
    {
        var price = Price.Create(chargeId, GlnNumber.Create("5790000432752"),
            PriceType.Tarif, description,
            Period.From(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        price.AddPricePoint(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), rate);
        return new PriceWithPoints(price);
    }

    private static List<PriceWithPoints> CreateMigratedPrices() => new()
    {
        CreateTariff("40000", "Nettarif C time", NettarifRate),
        CreateTariff("41000", "Systemtarif", SystemtarifRate),
        CreateTariff("42000", "Transmissionsnettarif", TransmissionRate),
        CreateTariff("EA-001", "Elafgift", ElafgiftRate),
    };

    private static List<SpotPrice> CreateSpotPrices(decimal avgPrice)
    {
        var spots = new List<SpotPrice>();
        for (int i = 0; i < HoursInFeb; i++)
        {
            spots.Add(SpotPrice.Create("DK1", Feb1.AddHours(i), avgPrice));
        }
        return spots;
    }

    private static SupplierMargin CreateMargin()
    {
        var productId = Guid.NewGuid();
        return SupplierMargin.Create(productId,
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), MarginRate);
    }

    /// <summary>
    /// Build a migrated settlement that matches what MigrationEndpoints creates when prices
    /// are migrated first: each tariff line has a real PriceId, spot/margin matched by Source.
    /// </summary>
    private static Settlement CreateMigratedSettlement(
        Supply supply, decimal kwhPerHour, List<PriceWithPoints> prices,
        List<SpotPrice> spotPrices, SupplierMargin margin,
        decimal? spotRate = null, decimal? marginRate = null)
    {
        var totalKwh = kwhPerHour * HoursInFeb;
        var effectiveSpotRate = spotRate ?? SpotPriceAvg;
        var effectiveMarginRate = marginRate ?? MarginRate;
        var settlement = Settlement.CreateMigrated(
            MpId, supply.Id, February, Guid.NewGuid(), 1,
            EnergyQuantity.Create(totalKwh), "BL-99001");

        // Spot line — imported from Xellent FlexBillingHistory.PowerExchangePrice × TimeValue
        settlement.AddLine(SettlementLine.CreateSpot(
            settlement.Id, "Spotpris (migreret)",
            EnergyQuantity.Create(totalKwh),
            effectiveSpotRate));

        // Margin line — imported from Xellent (CalculatedPrice - PowerExchangePrice) × TimeValue
        settlement.AddLine(SettlementLine.CreateMargin(
            settlement.Id, "Leverandørmargin (migreret)",
            EnergyQuantity.Create(totalKwh),
            effectiveMarginRate));

        // Tariff lines — WITH PriceId (because prices were migrated before settlements)
        foreach (var priceLink in prices)
        {
            var rate = priceLink.GetPriceAt(Feb1) ?? 0m;
            settlement.AddLine(SettlementLine.Create(
                settlement.Id, priceLink.Price.Id,
                $"{priceLink.Price.Description} (migreret)",
                EnergyQuantity.Create(totalKwh),
                rate));
        }

        return settlement;
    }

    // ============================================================================
    // PROOF TEST 1: Higher consumption → precise debit per line
    // ============================================================================

    [Fact]
    public void MigratedSettlement_HigherConsumption_ExactDeltaPerLine()
    {
        // ARRANGE — simulate a customer migrated from Xellent
        var supply = CreateSupply();
        var prices = CreateMigratedPrices();
        var spotPrices = CreateSpotPrices(SpotPriceAvg);
        var margin = CreateMargin();
        const decimal originalKwh = 1.0m;
        const decimal correctedKwh = 1.15m; // 15% higher — DataHub sent corrected readings

        // This is what was actually invoiced via Xellent (now imported to WattsOn)
        var migratedSettlement = CreateMigratedSettlement(supply, originalKwh, prices, spotPrices, margin);
        Assert.Equal(SettlementStatus.Migrated, migratedSettlement.Status);
        Assert.Equal(6, migratedSettlement.Lines.Count); // spot + margin + 4 tariffs

        // ACT — DataHub sends corrected time series v2
        migratedSettlement.MarkAdjusted(); // Must work for Migrated status
        Assert.Equal(SettlementStatus.Adjusted, migratedSettlement.Status);

        var correctedTs = CreateTimeSeries(correctedKwh, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, migratedSettlement, prices, spotPrices, margin, PricingModel.SpotAddon);

        // ASSERT — correction is a precise delta
        Assert.True(correction.IsCorrection);
        Assert.Equal(migratedSettlement.Id, correction.PreviousSettlementId);
        Assert.Equal(2, correction.TimeSeriesVersion);

        // Delta energy: 672h × (1.15 - 1.0) = 672 × 0.15 = 100.8 kWh
        var expectedDeltaKwh = HoursInFeb * (correctedKwh - originalKwh);
        Assert.Equal(expectedDeltaKwh, correction.TotalEnergy.Value);

        // Each line should have the exact delta quantity
        var spotLine = correction.Lines.First(l => l.Source == SettlementLineSource.SpotPrice);
        Assert.Equal(expectedDeltaKwh, spotLine.Quantity.Value);

        var marginLine = correction.Lines.First(l => l.Source == SettlementLineSource.SupplierMargin);
        Assert.Equal(expectedDeltaKwh, marginLine.Quantity.Value);

        // Tariff lines: delta quantity matches
        var nettarifLine = correction.Lines.First(l => l.Description.Contains("Nettarif"));
        Assert.Equal(expectedDeltaKwh, nettarifLine.Quantity.Value);

        var systemLine = correction.Lines.First(l => l.Description.Contains("Systemtarif"));
        Assert.Equal(expectedDeltaKwh, systemLine.Quantity.Value);

        var transmissionLine = correction.Lines.First(l => l.Description.Contains("Transmission"));
        Assert.Equal(expectedDeltaKwh, transmissionLine.Quantity.Value);

        var elafgiftLine = correction.Lines.First(l => l.Description.Contains("Elafgift"));
        Assert.Equal(expectedDeltaKwh, elafgiftLine.Quantity.Value);

        // All amounts should be positive (debit — customer owes more)
        Assert.True(correction.TotalAmount.Amount > 0,
            $"Expected positive outstanding (debit), got {correction.TotalAmount.Amount} DKK");
        foreach (var line in correction.Lines)
            Assert.True(line.Amount.Amount > 0, $"Line '{line.Description}' should be positive (debit)");

        // Total outstanding is sum of all line amounts
        Assert.Equal(correction.Lines.Sum(l => l.Amount.Amount), correction.TotalAmount.Amount);

        // Verify each delta amount is approximately correct
        // SettlementCalculator computes per-observation with avgUnitPrice rounding, so ±0.05 DKK tolerance
        AssertApprox(expectedDeltaKwh * NettarifRate, nettarifLine.Amount.Amount, 0.05m, "Nettarif");
        AssertApprox(expectedDeltaKwh * SystemtarifRate, systemLine.Amount.Amount, 0.05m, "Systemtarif");
        AssertApprox(expectedDeltaKwh * TransmissionRate, transmissionLine.Amount.Amount, 0.05m, "Transmission");
        AssertApprox(expectedDeltaKwh * ElafgiftRate, elafgiftLine.Amount.Amount, 0.05m, "Elafgift");
        AssertApprox(expectedDeltaKwh * SpotPriceAvg, spotLine.Amount.Amount, 0.05m, "Spot");
        AssertApprox(expectedDeltaKwh * MarginRate, marginLine.Amount.Amount, 0.05m, "Margin");
    }

    // ============================================================================
    // PROOF TEST 2: Lower consumption → precise credit per line
    // ============================================================================

    [Fact]
    public void MigratedSettlement_LowerConsumption_ExactCreditPerLine()
    {
        var supply = CreateSupply();
        var prices = CreateMigratedPrices();
        var spotPrices = CreateSpotPrices(SpotPriceAvg);
        var margin = CreateMargin();
        const decimal originalKwh = 1.5m;
        const decimal correctedKwh = 1.35m; // 10% lower — customer was overcharged

        var migratedSettlement = CreateMigratedSettlement(supply, originalKwh, prices, spotPrices, margin);
        migratedSettlement.MarkAdjusted();

        var correctedTs = CreateTimeSeries(correctedKwh, version: 2);
        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, migratedSettlement, prices, spotPrices, margin, PricingModel.SpotAddon);

        // Delta: 672 × (1.35 - 1.5) = 672 × -0.15 = -100.8 kWh
        var expectedDeltaKwh = HoursInFeb * (correctedKwh - originalKwh);
        Assert.Equal(expectedDeltaKwh, correction.TotalEnergy.Value);
        Assert.True(expectedDeltaKwh < 0);

        // Total should be negative (credit to customer)
        Assert.True(correction.TotalAmount.Amount < 0,
            $"Expected negative outstanding (credit), got {correction.TotalAmount.Amount} DKK");

        // Every line should be negative
        foreach (var line in correction.Lines)
        {
            Assert.True(line.Amount.Amount < 0,
                $"Line '{line.Description}' should be negative, got {line.Amount.Amount}");
        }

        // Precise nettarif credit (within rounding)
        var nettarifLine = correction.Lines.First(l => l.Description.Contains("Nettarif"));
        Assert.Equal(expectedDeltaKwh * NettarifRate, nettarifLine.Amount.Amount, precision: 2);
    }

    // ============================================================================
    // PROOF TEST 3: Show actual DKK amounts for a realistic scenario
    // ============================================================================

    [Fact]
    public void MigratedSettlement_RealisticScenario_ShowExactOutstanding()
    {
        // Realistic scenario: apartment, ~250 kWh/month, correction of +12 kWh
        var supply = CreateSupply();
        var prices = CreateMigratedPrices();
        var spotPrices = CreateSpotPrices(0.52m); // 52 øre/kWh spot
        var margin = CreateMargin();

        const decimal originalKwhPerHour = 0.372m; // ~250 kWh/month
        const decimal correctedKwhPerHour = 0.3899m; // ~262 kWh/month (+12 kWh)

        var migratedSettlement = CreateMigratedSettlement(
            supply, originalKwhPerHour, prices, spotPrices, margin, spotRate: 0.52m);
        migratedSettlement.MarkAdjusted();

        var correctedTs = CreateTimeSeries(correctedKwhPerHour, version: 2);
        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, migratedSettlement, prices, spotPrices, margin, PricingModel.SpotAddon);

        // Delta kWh: 672 × (0.3899 - 0.372) = 672 × 0.0179 = 12.0288 kWh
        var deltaKwh = correction.TotalEnergy.Value;
        Assert.True(deltaKwh > 11m && deltaKwh < 13m,
            $"Expected ~12 kWh delta, got {deltaKwh}");

        // All 6 lines should be present in correction
        Assert.Equal(6, correction.Lines.Count);

        // Outstanding breakdown (approximate, per-hour rounding):
        // Spot:          ~12 × 0.52 = ~6.25 DKK
        // Margin:        ~12 × 0.04 = ~0.48 DKK
        // Nettarif:      ~12 × 0.2616 = ~3.15 DKK
        // Systemtarif:   ~12 × 0.054 = ~0.65 DKK
        // Transmission:  ~12 × 0.049 = ~0.59 DKK
        // Elafgift:      ~12 × 0.008 = ~0.10 DKK
        // Total:         ~11.22 DKK

        var totalOutstanding = correction.TotalAmount.Amount;
        Assert.True(totalOutstanding > 10m && totalOutstanding < 13m,
            $"Expected ~11.22 DKK outstanding, got {totalOutstanding}");
        Assert.True(totalOutstanding > 0, "Customer owes more (debit)");

        // Each component is present and positive
        Assert.True(correction.Lines.All(l => l.Amount.Amount > 0));
    }

    // ============================================================================
    // PROOF TEST 4: Fixed-price product migration correction
    // ============================================================================

    [Fact]
    public void MigratedSettlement_FixedPriceProduct_CorrectDelta()
    {
        var supply = CreateSupply();
        var prices = CreateMigratedPrices();
        const decimal fixedRate = 0.89m; // DKK/kWh fixed price

        const decimal originalKwh = 1.0m;
        const decimal correctedKwh = 1.2m;

        // Migrated fixed-price settlement: no spot, margin IS the full electricity cost
        var totalKwh = originalKwh * HoursInFeb;
        var settlement = Settlement.CreateMigrated(
            MpId, supply.Id, February, Guid.NewGuid(), 1,
            EnergyQuantity.Create(totalKwh), "BL-FIXED-001");

        // Fixed price line (as SupplierMargin source — that's what SettlementCalculator uses)
        settlement.AddLine(SettlementLine.CreateMargin(
            settlement.Id, "Elpris (fast) (migreret)",
            EnergyQuantity.Create(totalKwh), fixedRate));

        // Tariff lines with PriceIds
        foreach (var priceLink in prices)
        {
            var rate = priceLink.GetPriceAt(Feb1) ?? 0m;
            settlement.AddLine(SettlementLine.Create(
                settlement.Id, priceLink.Price.Id,
                $"{priceLink.Price.Description} (migreret)",
                EnergyQuantity.Create(totalKwh), rate));
        }

        settlement.MarkAdjusted();

        var fixedMargin = SupplierMargin.Create(Guid.NewGuid(),
            new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), fixedRate);
        var correctedTs = CreateTimeSeries(correctedKwh, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, settlement, prices, Array.Empty<SpotPrice>(),
            fixedMargin, PricingModel.Fixed);

        var expectedDeltaKwh = HoursInFeb * (correctedKwh - originalKwh);
        Assert.Equal(expectedDeltaKwh, correction.TotalEnergy.Value);

        // Fixed electricity delta
        var elLine = correction.Lines.First(l => l.Source == SettlementLineSource.SupplierMargin);
        Assert.Equal(expectedDeltaKwh * fixedRate, elLine.Amount.Amount, precision: 2);

        Assert.True(correction.TotalAmount.Amount > 0);
    }

    // ============================================================================
    // PROOF TEST 5: Without price migration, tariff line matching degrades
    //               (proves WHY migrating prices first matters)
    // ============================================================================

    [Fact]
    public void MigratedSettlement_WithoutPriceMigration_TariffMatchingDegrades()
    {
        var supply = CreateSupply();
        var prices = CreateMigratedPrices();
        var spotPrices = CreateSpotPrices(SpotPriceAvg);
        var margin = CreateMargin();

        // Create migrated settlement with NULL PriceIds (no price migration)
        var totalKwh = 1.0m * HoursInFeb;
        var settlement = Settlement.CreateMigrated(
            MpId, supply.Id, February, Guid.NewGuid(), 1,
            EnergyQuantity.Create(totalKwh), "BL-NOPRICE-001");

        // Spot and margin use Source-based matching — these work regardless
        settlement.AddLine(SettlementLine.CreateSpot(settlement.Id, "Spotpris", EnergyQuantity.Create(totalKwh), SpotPriceAvg));
        settlement.AddLine(SettlementLine.CreateMargin(settlement.Id, "Leverandørmargin", EnergyQuantity.Create(totalKwh), MarginRate));
        // These use CreateMigrated — PriceId is NULL
        settlement.AddLine(SettlementLine.CreateMigrated(settlement.Id, "Nettarif [40000]", EnergyQuantity.Create(totalKwh), NettarifRate));
        settlement.AddLine(SettlementLine.CreateMigrated(settlement.Id, "Systemtarif [41000]", EnergyQuantity.Create(totalKwh), SystemtarifRate));

        settlement.MarkAdjusted();

        var correctedTs = CreateTimeSeries(1.1m, version: 2);
        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, settlement, prices, spotPrices, margin, PricingModel.SpotAddon);

        // Spot and margin lines STILL match correctly (both use Source matching with null PriceId) ✅
        var spotLine = correction.Lines.First(l => l.Source == SettlementLineSource.SpotPrice);
        var expectedSpotDelta = HoursInFeb * 0.1m * SpotPriceAvg;
        Assert.Equal(expectedSpotDelta, spotLine.Amount.Amount, precision: 2);

        var marginLine = correction.Lines.First(l => l.Source == SettlementLineSource.SupplierMargin);
        Assert.Equal(HoursInFeb * 0.1m * MarginRate, marginLine.Amount.Amount, precision: 2);

        // BUT: tariff lines have PriceId mismatch:
        // - New calculated lines have PriceId = some-guid
        // - Migrated lines have PriceId = null
        // They DON'T match → correction treats original tariff amounts as 0 → delta = FULL new amount
        var tariffLines = correction.Lines.Where(l => l.Source == SettlementLineSource.DataHubCharge).ToList();

        // Each tariff line shows the FULL 1.1 kWh/h amount, not just the 0.1 delta
        // This is inflated — proof that price migration is necessary for correct tariff deltas
        var fullEnergy = HoursInFeb * 1.1m; // 739.2 (wrong — should be 67.2 delta)
        foreach (var line in tariffLines)
        {
            Assert.Equal(fullEnergy, line.Quantity.Value);
        }

        // Without price migration: tariff correction amounts are WRONG
        // With price migration: they'd be exact deltas (see test 1 above)
        var correctTotal = HoursInFeb * 0.1m * (SpotPriceAvg + MarginRate + NettarifRate + SystemtarifRate);
        Assert.True(correction.TotalAmount.Amount > correctTotal,
            "Without price migration, total is inflated due to tariff mismatch");
    }

    // ============================================================================
    // PROOF TEST 6: Migrated settlement status transitions
    // ============================================================================

    [Fact]
    public void MigratedSettlement_StatusTransitions_WorkCorrectly()
    {
        var settlement = Settlement.CreateMigrated(
            MpId, Guid.NewGuid(), February, Guid.NewGuid(), 1,
            EnergyQuantity.Create(672m), "BL-12345");

        // Initial state
        Assert.Equal(SettlementStatus.Migrated, settlement.Status);
        Assert.Equal("BL-12345", settlement.ExternalInvoiceReference);
        Assert.NotNull(settlement.InvoicedAt); // Migrated settlements are "already invoiced" in old system

        // Can be marked adjusted (for corrections)
        settlement.MarkAdjusted();
        Assert.Equal(SettlementStatus.Adjusted, settlement.Status);

        // Cannot re-adjust
        Assert.Throws<InvalidOperationException>(() => settlement.MarkAdjusted());
    }

    [Fact]
    public void CalculatedSettlement_CannotBeAdjustedDirectly()
    {
        var supply = CreateSupply();
        var prices = new List<PriceWithPoints> { CreateTariff("40000", "Nettarif", NettarifRate) };
        var ts = CreateTimeSeries(1.0m, 1);
        var settlement = SettlementCalculator.Calculate(ts, supply, prices, Array.Empty<SpotPrice>());

        // Calculated → must be invoiced first → then adjusted
        Assert.Equal(SettlementStatus.Calculated, settlement.Status);
        Assert.Throws<InvalidOperationException>(() => settlement.MarkAdjusted());
    }

    private static void AssertApprox(decimal expected, decimal actual, decimal tolerance, string label)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance,
            $"{label}: expected ≈{expected:F4} DKK, got {actual:F4} DKK (tolerance ±{tolerance})");
    }
}
