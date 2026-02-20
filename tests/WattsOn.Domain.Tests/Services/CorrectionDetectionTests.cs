using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

/// <summary>
/// Tests for the complete correction detection flow:
/// 1. Original settlement calculated
/// 2. Settlement marked as invoiced
/// 3. New time series version arrives (correction)
/// 4. Delta calculated against original
/// 5. Adjustment settlement created
///
/// This mirrors what SettlementWorker does when DataHub sends corrected metered data.
/// </summary>
public class CorrectionDetectionTests
{
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mar1 = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Period February = Period.Create(Feb1, Mar1);
    private static readonly Guid MpId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static Supply CreateSupply() =>
        Supply.Create(MpId, CustomerId, Period.From(Feb1));

    private static TimeSeries CreateTimeSeries(decimal kwhPerHour, int version)
    {
        var ts = TimeSeries.Create(MpId, February, Resolution.PT1H, version, $"TX-{version:D3}");
        var hours = (int)(Mar1 - Feb1).TotalHours; // 672 hours in February 2026

        for (int i = 0; i < hours; i++)
        {
            ts.AddObservation(
                Feb1.AddHours(i),
                EnergyQuantity.Create(kwhPerHour),
                version == 1 ? QuantityQuality.Measured : QuantityQuality.Revised);
        }

        return ts;
    }

    private static PriceWithPoints CreateNettarif() =>
        CreateFlatTariff("NT-001", "Nettarif C-kunde", 0.2616m);

    private static PriceWithPoints CreateSystemtarif() =>
        CreateFlatTariff("ST-001", "Systemtarif", 0.054m);

    private static PriceWithPoints CreateElafgift() =>
        CreateFlatTariff("EA-001", "Elafgift", 0.008m);

    private static PriceWithPoints CreateLeverandorMargin() =>
        CreateFlatTariff("LM-001", "Leverandørmargin", 0.15m);

    private static PriceWithPoints CreateSubscription() =>
        CreateAbonnement("AB-001", "Månedligt abonnement", 23.20m);

    private static PriceWithPoints CreateFlatTariff(string chargeId, string description, decimal pricePerKwh)
    {
        var pris = Price.Create(chargeId, GlnNumber.Create("5790001330552"),
            PriceType.Tarif, description,
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        pris.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), pricePerKwh);
        return new PriceWithPoints(pris);
    }

    private static PriceWithPoints CreateAbonnement(string chargeId, string description, decimal dailyPrice)
    {
        var pris = Price.Create(chargeId, GlnNumber.Create("5790001330552"),
            PriceType.Abonnement, description,
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        pris.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), dailyPrice);
        return new PriceWithPoints(pris);
    }

    private static List<PriceWithPoints> AllPrices() => new()
    {
        CreateNettarif(),
        CreateSystemtarif(),
        CreateElafgift(),
        CreateLeverandorMargin(),
        CreateSubscription()
    };

    // --- Full correction flow ---

    [Fact]
    public void FullCorrectionFlow_HigherConsumption_CreatesDebitNote()
    {
        var supply = CreateSupply();
        var prices = AllPrices();

        // Step 1: Original settlement (1.0 kWh/h)
        var originalTs = CreateTimeSeries(1.0m, version: 1);
        var original = SettlementCalculator.Calculate(originalTs, supply, prices, Array.Empty<SpotPrice>());

        Assert.Equal(SettlementStatus.Calculated, original.Status);
        Assert.False(original.IsCorrection);
        Assert.True(original.TotalAmount.Amount > 0);

        // Step 2: Mark as invoiced
        original.MarkInvoiced("INV-2026-0042");
        Assert.Equal(SettlementStatus.Invoiced, original.Status);

        // Step 3: Correction arrives — 10% more consumption
        var correctedTs = CreateTimeSeries(1.1m, version: 2);
        originalTs.Supersede();

        Assert.False(originalTs.IsLatest);
        Assert.True(correctedTs.IsLatest);

        // Step 4: Mark original as adjusted
        original.MarkAdjusted();
        Assert.Equal(SettlementStatus.Adjusted, original.Status);

        // Step 5: Calculate correction
        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, original, prices, Array.Empty<SpotPrice>());

        Assert.True(correction.IsCorrection);
        Assert.Equal(original.Id, correction.PreviousSettlementId);
        Assert.Equal(2, correction.TimeSeriesVersion);
        Assert.Equal(SettlementStatus.Calculated, correction.Status);

        // Delta should be positive (higher consumption → debit note)
        Assert.True(correction.TotalAmount.Amount > 0);

        // Delta energy should be ~10% of original
        var expectedDeltaEnergy = 672m * 0.1m; // 672 hours × 0.1 kWh
        Assert.Equal(expectedDeltaEnergy, correction.TotalEnergy.Value);
    }

    [Fact]
    public void FullCorrectionFlow_LowerConsumption_CreatesCreditNote()
    {
        var supply = CreateSupply();
        var prices = AllPrices();

        // Original: 1.5 kWh/h
        var originalTs = CreateTimeSeries(1.5m, version: 1);
        var original = SettlementCalculator.Calculate(originalTs, supply, prices, Array.Empty<SpotPrice>());
        original.MarkInvoiced("INV-2026-0043");
        original.MarkAdjusted();

        // Correction: 1.2 kWh/h (lower — customer overpaid)
        var correctedTs = CreateTimeSeries(1.2m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, original, prices, Array.Empty<SpotPrice>());

        // Delta should be negative (lower consumption → credit note)
        Assert.True(correction.TotalAmount.Amount < 0);
        Assert.True(correction.TotalEnergy.Value < 0);
    }

    [Fact]
    public void CorrectionFlow_OnlyTariffLines_SubscriptionUnchanged()
    {
        var supply = CreateSupply();
        var prices = AllPrices();

        // Original: 1.0 kWh/h
        var originalTs = CreateTimeSeries(1.0m, version: 1);
        var original = SettlementCalculator.Calculate(originalTs, supply, prices, Array.Empty<SpotPrice>());
        original.MarkInvoiced("INV-2026-0044");

        // Correction: 1.1 kWh/h — only tariff-based lines should have deltas
        var correctedTs = CreateTimeSeries(1.1m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, original, prices, Array.Empty<SpotPrice>());

        // All correction lines should be tariff adjustments (subscription has no energy delta)
        // Subscription uses days, not kWh — so its delta should be zero and excluded
        foreach (var line in correction.Lines)
        {
            Assert.Contains("justering", line.Description);
            // Subscription line should not appear (0 delta excluded)
            Assert.DoesNotContain("Månedligt abonnement", line.Description);
        }
    }

    [Fact]
    public void CorrectionFlow_PreservesOriginalInvoiceReference()
    {
        var supply = CreateSupply();
        var prices = new List<PriceWithPoints> { CreateNettarif() };

        var originalTs = CreateTimeSeries(1.0m, version: 1);
        var original = SettlementCalculator.Calculate(originalTs, supply, prices, Array.Empty<SpotPrice>());
        original.MarkInvoiced("INV-ORIGINAL-REF");
        original.MarkAdjusted();

        // Original should still have its invoice reference
        Assert.Equal("INV-ORIGINAL-REF", original.ExternalInvoiceReference);
        Assert.NotNull(original.InvoicedAt);
    }

    [Fact]
    public void CorrectionFlow_CannotCorrectUnInvoicedSettlement()
    {
        var supply = CreateSupply();
        var original = SettlementCalculator.Calculate(
            CreateTimeSeries(1.0m, 1), supply, new List<PriceWithPoints> { CreateNettarif() }, Array.Empty<SpotPrice>());

        // Cannot mark as adjusted without being invoiced/migrated first
        Assert.Throws<InvalidOperationException>(() => original.MarkAdjusted());
    }

    [Fact]
    public void CorrectionFlow_MigratedSettlement_CanBeMarkedAdjusted()
    {
        var migrated = Settlement.CreateMigrated(
            MpId, Guid.NewGuid(), February, Guid.NewGuid(), 1,
            EnergyQuantity.Create(672m), "BL-12345");

        Assert.Equal(SettlementStatus.Migrated, migrated.Status);

        // Migrated settlements can be corrected just like invoiced ones
        migrated.MarkAdjusted();
        Assert.Equal(SettlementStatus.Adjusted, migrated.Status);
    }

    [Fact]
    public void TimeSeriesVersioning_SupersedeMarksNotLatest()
    {
        var ts1 = CreateTimeSeries(1.0m, version: 1);
        Assert.True(ts1.IsLatest);

        ts1.Supersede();
        Assert.False(ts1.IsLatest);

        var ts2 = CreateTimeSeries(1.1m, version: 2);
        Assert.True(ts2.IsLatest);
        Assert.Equal(2, ts2.Version);
    }

    [Fact]
    public void CorrectionFlow_MultiplePrices_AllDeltasCorrect()
    {
        var supply = CreateSupply();
        var nettarif = CreateNettarif();
        var systemtarif = CreateSystemtarif();
        var prices = new List<PriceWithPoints> { nettarif, systemtarif };

        // Original: 1.0 kWh/h for February (672 hours)
        var originalTs = CreateTimeSeries(1.0m, version: 1);
        var original = SettlementCalculator.Calculate(originalTs, supply, prices, Array.Empty<SpotPrice>());
        original.MarkInvoiced("INV-MULTI");

        // Correction: 1.2 kWh/h (20% more)
        var correctedTs = CreateTimeSeries(1.2m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, original, prices, Array.Empty<SpotPrice>());

        Assert.Equal(2, correction.Lines.Count);

        // Delta energy: 672 × 0.2 = 134.4 kWh
        var expectedDeltaEnergy = 672m * 0.2m;
        Assert.Equal(expectedDeltaEnergy, correction.TotalEnergy.Value);

        // Nettarif delta: 134.4 × 0.2616 = 35.16 (approx)
        var nettarifLine = correction.Lines.First(l => l.Description.Contains("Nettarif"));
        Assert.Equal(expectedDeltaEnergy, nettarifLine.Quantity.Value);

        // Systemtarif delta: 134.4 × 0.054 = 7.26 (approx)
        var systemLine = correction.Lines.First(l => l.Description.Contains("Systemtarif"));
        Assert.Equal(expectedDeltaEnergy, systemLine.Quantity.Value);

        // Total: sum of all line amounts
        Assert.Equal(correction.Lines.Sum(l => l.Amount.Amount), correction.TotalAmount.Amount);
    }

    // --- Invoicing handoff tests ---

    [Fact]
    public void InvoicingHandoff_MarkInvoiced_SetsAllFields()
    {
        var supply = CreateSupply();
        var ts = CreateTimeSeries(1.0m, 1);
        var settlement = SettlementCalculator.Calculate(ts, supply, AllPrices(), Array.Empty<SpotPrice>());

        var before = DateTimeOffset.UtcNow;
        settlement.MarkInvoiced("INV-2026-0001");
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(SettlementStatus.Invoiced, settlement.Status);
        Assert.Equal("INV-2026-0001", settlement.ExternalInvoiceReference);
        Assert.NotNull(settlement.InvoicedAt);
        Assert.InRange(settlement.InvoicedAt!.Value, before, after);
    }

    [Fact]
    public void InvoicingHandoff_DoubleInvoice_ThrowsConflict()
    {
        var supply = CreateSupply();
        var ts = CreateTimeSeries(1.0m, 1);
        var settlement = SettlementCalculator.Calculate(ts, supply, new List<PriceWithPoints> { CreateNettarif() }, Array.Empty<SpotPrice>());

        settlement.MarkInvoiced("INV-FIRST");

        Assert.Throws<InvalidOperationException>(() =>
            settlement.MarkInvoiced("INV-SECOND"));
    }

    [Fact]
    public void InvoicingHandoff_CorrectionIsCalculated_CanBeInvoiced()
    {
        var supply = CreateSupply();
        var prices = new List<PriceWithPoints> { CreateNettarif() };

        var originalTs = CreateTimeSeries(1.0m, version: 1);
        var original = SettlementCalculator.Calculate(originalTs, supply, prices, Array.Empty<SpotPrice>());
        original.MarkInvoiced("INV-ORIG");
        original.MarkAdjusted();

        var correctedTs = CreateTimeSeries(1.1m, version: 2);
        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, original, prices, Array.Empty<SpotPrice>());

        // Correction starts as Calculated — can be invoiced by external system
        Assert.Equal(SettlementStatus.Calculated, correction.Status);

        correction.MarkInvoiced("INV-CORRECTION-001");
        Assert.Equal(SettlementStatus.Invoiced, correction.Status);
    }
}
