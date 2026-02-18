using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Tests.Services;

public class SettlementCalculatorTests
{
    private static readonly DateTimeOffset Jan1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Feb1 = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Period January = Period.Create(Jan1, Feb1);

    private static readonly Guid MpId = Guid.NewGuid();
    private static readonly Guid KundeId = Guid.NewGuid();
    private static readonly Guid AktørId = Guid.NewGuid();

    private static Leverance CreateLeverance() =>
        Leverance.Create(MpId, KundeId, AktørId, Period.From(Jan1));

    /// <summary>Create a simple hourly time series for January with constant consumption</summary>
    private static Tidsserie CreateJanuaryTimeSeries(decimal kwhPerHour = 1.0m, int version = 1)
    {
        var ts = Tidsserie.Create(MpId, January, Resolution.PT1H, version, "TX-001");
        var hours = (int)(Feb1 - Jan1).TotalHours; // 744 hours in January 2026

        for (int i = 0; i < hours; i++)
        {
            ts.AddObservation(
                Jan1.AddHours(i),
                EnergyQuantity.Create(kwhPerHour),
                QuantityQuality.Målt);
        }

        return ts;
    }

    /// <summary>Create a flat tariff price (same price every hour)</summary>
    private static PriceWithPoints CreateFlatTariff(string chargeId, string description, decimal pricePerKwh)
    {
        var pris = Pris.Create(
            chargeId,
            GlnNumber.Create("5790001330552"),
            PriceType.Tarif,
            description,
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        // Single price point = flat rate
        pris.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), pricePerKwh);

        return new PriceWithPoints(pris);
    }

    /// <summary>Create a subscription price (flat daily fee)</summary>
    private static PriceWithPoints CreateSubscription(string chargeId, string description, decimal dailyPrice)
    {
        var pris = Pris.Create(
            chargeId,
            GlnNumber.Create("5790001330552"),
            PriceType.Abonnement,
            description,
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        pris.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), dailyPrice);

        return new PriceWithPoints(pris);
    }

    // --- Basic tariff calculation ---

    [Fact]
    public void Calculate_SingleFlatTariff_CorrectTotal()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 1.0m);
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        var afregning = SettlementCalculator.Calculate(ts, leverance, [nettarif]);

        // 744 hours × 1.0 kWh × 0.25 DKK = 186.00 DKK
        Assert.Equal(186.00m, afregning.TotalAmount.Amount);
        Assert.Single(afregning.Lines);
        Assert.Equal("Nettarif", afregning.Lines[0].Description);
    }

    [Fact]
    public void Calculate_MultipleTariffs_SumsCorrectly()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 2.0m);
        var leverance = CreateLeverance();

        var prices = new List<PriceWithPoints>
        {
            CreateFlatTariff("NT-001", "Nettarif", 0.25m),
            CreateFlatTariff("ST-001", "Systemtarif", 0.054m),
            CreateFlatTariff("EA-001", "Elafgift", 0.763m),
        };

        var afregning = SettlementCalculator.Calculate(ts, leverance, prices);

        // 744h × 2.0 kWh = 1488 kWh total
        // Nettarif:    1488 × 0.25  = 372.00
        // Systemtarif: 1488 × 0.054 =  80.35
        // Elafgift:    1488 × 0.763 = 1135.34
        // Total: 1587.69 (with rounding)
        Assert.Equal(3, afregning.Lines.Count);
        Assert.Equal(1488.000m, afregning.TotalEnergy.Value);

        var total = afregning.Lines.Sum(l => l.Amount.Amount);
        Assert.Equal(afregning.TotalAmount.Amount, total);
    }

    [Fact]
    public void Calculate_Subscription_DailyCharge()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 1.0m);
        var leverance = CreateLeverance();
        var subscription = CreateSubscription("AB-001", "Net Abonnement", 2.50m);

        var afregning = SettlementCalculator.Calculate(ts, leverance, [subscription]);

        // January = 31 days × 2.50 DKK = 77.50 DKK
        Assert.Equal(77.50m, afregning.TotalAmount.Amount);
    }

    [Fact]
    public void Calculate_MixedTariffAndSubscription()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 1.0m);
        var leverance = CreateLeverance();

        var prices = new List<PriceWithPoints>
        {
            CreateFlatTariff("NT-001", "Nettarif", 0.25m),
            CreateSubscription("AB-001", "Net Abonnement", 2.50m),
        };

        var afregning = SettlementCalculator.Calculate(ts, leverance, prices);

        // Nettarif: 744 × 1.0 × 0.25 = 186.00
        // Abonnement: 31 × 2.50 = 77.50
        // Total: 263.50
        Assert.Equal(2, afregning.Lines.Count);
        Assert.Equal(263.50m, afregning.TotalAmount.Amount);
    }

    // --- Metadata ---

    [Fact]
    public void Calculate_SetsCorrectMetadata()
    {
        var ts = CreateJanuaryTimeSeries();
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.10m);

        var afregning = SettlementCalculator.Calculate(ts, leverance, [nettarif]);

        Assert.Equal(MpId, afregning.MålepunktId);
        Assert.Equal(leverance.Id, afregning.LeveranceId);
        Assert.Equal(ts.Id, afregning.TidsserieId);
        Assert.Equal(1, afregning.TidsserieVersion);
        Assert.Equal(Jan1, afregning.SettlementPeriod.Start);
        Assert.Equal(Feb1, afregning.SettlementPeriod.End);
        Assert.Equal(AfregningStatus.Beregnet, afregning.Status);
        Assert.False(afregning.IsCorrection);
    }

    [Fact]
    public void Calculate_TotalEnergyMatchesTimeSeries()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 3.5m);
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.10m);

        var afregning = SettlementCalculator.Calculate(ts, leverance, [nettarif]);

        // 744 hours × 3.5 kWh = 2604.0
        Assert.Equal(2604.000m, afregning.TotalEnergy.Value);
    }

    // --- Time-varying tariffs ---

    [Fact]
    public void Calculate_TimeVaryingTariff_UsesCorrectPricePerHour()
    {
        // Create a short time series: just 4 hours
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);
        var period = Period.Create(start, end);

        var ts = Tidsserie.Create(MpId, period, Resolution.PT1H, 1);
        ts.AddObservation(start.AddHours(0), EnergyQuantity.Create(1.0m), QuantityQuality.Målt); // hour 0
        ts.AddObservation(start.AddHours(1), EnergyQuantity.Create(1.0m), QuantityQuality.Målt); // hour 1
        ts.AddObservation(start.AddHours(2), EnergyQuantity.Create(1.0m), QuantityQuality.Målt); // hour 2
        ts.AddObservation(start.AddHours(3), EnergyQuantity.Create(1.0m), QuantityQuality.Målt); // hour 3

        // Price varies: 0.10 for hours 0-1, 0.30 for hours 2-3
        var pris = Pris.Create("TV-001", GlnNumber.Create("5790001330552"),
            PriceType.Tarif, "Tidsvarieret tarif",
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        pris.AddPricePoint(start.AddHours(0), 0.10m);
        pris.AddPricePoint(start.AddHours(2), 0.30m);
        var priceLink = new PriceWithPoints(pris);

        var leverance = CreateLeverance();
        var afregning = SettlementCalculator.Calculate(ts, leverance, [priceLink]);

        // 2 × 1.0 × 0.10 + 2 × 1.0 × 0.30 = 0.20 + 0.60 = 0.80
        Assert.Equal(0.80m, afregning.TotalAmount.Amount);
    }

    // --- Edge cases ---

    [Fact]
    public void Calculate_EmptyTimeSeries_Throws()
    {
        var ts = Tidsserie.Create(MpId, January, Resolution.PT1H, 1);
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        Assert.Throws<InvalidOperationException>(() =>
            SettlementCalculator.Calculate(ts, leverance, [nettarif]));
    }

    [Fact]
    public void Calculate_NoPrices_ZeroTotal()
    {
        var ts = CreateJanuaryTimeSeries();
        var leverance = CreateLeverance();

        var afregning = SettlementCalculator.Calculate(ts, leverance, []);

        Assert.Equal(0m, afregning.TotalAmount.Amount);
        Assert.Empty(afregning.Lines);
    }

    [Fact]
    public void Calculate_GebyrPriceType_IsIgnored()
    {
        var ts = CreateJanuaryTimeSeries();
        var leverance = CreateLeverance();

        var pris = Pris.Create("GB-001", GlnNumber.Create("5790001330552"),
            PriceType.Gebyr, "Tilslutningsgebyr",
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        pris.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), 500m);
        var fee = new PriceWithPoints(pris);

        var afregning = SettlementCalculator.Calculate(ts, leverance, [fee]);

        Assert.Equal(0m, afregning.TotalAmount.Amount);
        Assert.Empty(afregning.Lines);
    }

    // --- Correction calculations ---

    [Fact]
    public void CalculateCorrection_ProducesDelta()
    {
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        // Original: 1.0 kWh/h for January
        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, leverance, [nettarif]);
        // Original: 744 × 1.0 × 0.25 = 186.00

        // Corrected data: 1.5 kWh/h (50% more consumption)
        var correctedTs = CreateJanuaryTimeSeries(kwhPerHour: 1.5m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, leverance, originalSettlement, [nettarif]);

        Assert.True(correction.IsCorrection);
        Assert.Equal(originalSettlement.Id, correction.PreviousAfregningId);
        Assert.Equal(2, correction.TidsserieVersion);
        Assert.Equal(AfregningStatus.Beregnet, correction.Status);

        // Delta: (744 × 1.5 × 0.25) - (744 × 1.0 × 0.25) = 279.00 - 186.00 = 93.00
        Assert.Equal(93.00m, correction.TotalAmount.Amount);

        // Delta energy: 744 × (1.5 - 1.0) = 372.0
        Assert.Equal(372.000m, correction.TotalEnergy.Value);
    }

    [Fact]
    public void CalculateCorrection_LowerConsumption_NegativeDelta()
    {
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        // Original: 2.0 kWh/h
        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 2.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, leverance, [nettarif]);

        // Corrected: 1.5 kWh/h (lower — customer overpaid)
        var correctedTs = CreateJanuaryTimeSeries(kwhPerHour: 1.5m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, leverance, originalSettlement, [nettarif]);

        // Delta: (744 × 1.5 × 0.25) - (744 × 2.0 × 0.25) = 279.00 - 372.00 = -93.00
        Assert.Equal(-93.00m, correction.TotalAmount.Amount);
        Assert.Equal(-372.000m, correction.TotalEnergy.Value);
    }

    [Fact]
    public void CalculateCorrection_NoChange_EmptyLines()
    {
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, leverance, [nettarif]);

        // Same data, new version — nothing changed
        var sameTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            sameTs, leverance, originalSettlement, [nettarif]);

        Assert.True(correction.IsCorrection);
        Assert.Equal(0m, correction.TotalAmount.Amount);
        Assert.Empty(correction.Lines); // No delta → no lines
    }

    [Fact]
    public void CalculateCorrection_MultiplePrices_CorrectDeltas()
    {
        var leverance = CreateLeverance();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);
        var systemtarif = CreateFlatTariff("ST-001", "Systemtarif", 0.054m);
        var prices = new List<PriceWithPoints> { nettarif, systemtarif };

        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, leverance, prices);

        // 10% more consumption
        var correctedTs = CreateJanuaryTimeSeries(kwhPerHour: 1.1m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, leverance, originalSettlement, prices);

        Assert.Equal(2, correction.Lines.Count);

        // Each line should be ~10% of the original
        var nettarifDelta = correction.Lines.First(l => l.Description.Contains("Nettarif"));
        var systemtarifDelta = correction.Lines.First(l => l.Description.Contains("Systemtarif"));

        // 744 × 0.1 × 0.25 = 18.60
        Assert.Equal(18.60m, nettarifDelta.Amount.Amount);
        // 744 × 1.1 × 0.054 = 44.19, minus original 744 × 1.0 × 0.054 = 40.18, delta = 4.01
        Assert.Equal(4.01m, systemtarifDelta.Amount.Amount);
    }
}
