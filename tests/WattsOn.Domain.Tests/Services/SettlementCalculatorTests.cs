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
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static Supply CreateSupply() =>
        Supply.Create(MpId, CustomerId, Period.From(Jan1));

    /// <summary>Create a simple hourly time series for January with constant consumption</summary>
    private static TimeSeries CreateJanuaryTimeSeries(decimal kwhPerHour = 1.0m, int version = 1)
    {
        var ts = TimeSeries.Create(MpId, January, Resolution.PT1H, version, "TX-001");
        var hours = (int)(Feb1 - Jan1).TotalHours; // 744 hours in January 2026

        for (int i = 0; i < hours; i++)
        {
            ts.AddObservation(
                Jan1.AddHours(i),
                EnergyQuantity.Create(kwhPerHour),
                QuantityQuality.Measured);
        }

        return ts;
    }

    /// <summary>Create a flat tariff price (same price every hour)</summary>
    private static PriceWithPoints CreateFlatTariff(string chargeId, string description, decimal pricePerKwh)
    {
        var pris = Price.Create(
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
        var pris = Price.Create(
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
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        var settlement = SettlementCalculator.Calculate(ts, supply, [nettarif], Array.Empty<SpotPrice>());

        // 744 hours × 1.0 kWh × 0.25 DKK = 186.00 DKK
        Assert.Equal(186.00m, settlement.TotalAmount.Amount);
        Assert.Single(settlement.Lines);
        Assert.Equal("Nettarif", settlement.Lines[0].Description);
    }

    [Fact]
    public void Calculate_MultipleTariffs_SumsCorrectly()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 2.0m);
        var supply = CreateSupply();

        var prices = new List<PriceWithPoints>
        {
            CreateFlatTariff("NT-001", "Nettarif", 0.25m),
            CreateFlatTariff("ST-001", "Systemtarif", 0.054m),
            CreateFlatTariff("EA-001", "Elafgift", 0.763m),
        };

        var settlement = SettlementCalculator.Calculate(ts, supply, prices, Array.Empty<SpotPrice>());

        // 744h × 2.0 kWh = 1488 kWh total
        // Nettarif:    1488 × 0.25  = 372.00
        // Systemtarif: 1488 × 0.054 =  80.35
        // Elafgift:    1488 × 0.763 = 1135.34
        // Total: 1587.69 (with rounding)
        Assert.Equal(3, settlement.Lines.Count);
        Assert.Equal(1488.000m, settlement.TotalEnergy.Value);

        var total = settlement.Lines.Sum(l => l.Amount.Amount);
        Assert.Equal(settlement.TotalAmount.Amount, total);
    }

    [Fact]
    public void Calculate_Subscription_DailyCharge()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 1.0m);
        var supply = CreateSupply();
        var subscription = CreateSubscription("AB-001", "Net Abonnement", 2.50m);

        var settlement = SettlementCalculator.Calculate(ts, supply, [subscription], Array.Empty<SpotPrice>());

        // January = 31 days × 2.50 DKK = 77.50 DKK
        Assert.Equal(77.50m, settlement.TotalAmount.Amount);
    }

    [Fact]
    public void Calculate_MixedTariffAndSubscription()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 1.0m);
        var supply = CreateSupply();

        var prices = new List<PriceWithPoints>
        {
            CreateFlatTariff("NT-001", "Nettarif", 0.25m),
            CreateSubscription("AB-001", "Net Abonnement", 2.50m),
        };

        var settlement = SettlementCalculator.Calculate(ts, supply, prices, Array.Empty<SpotPrice>());

        // Nettarif: 744 × 1.0 × 0.25 = 186.00
        // Abonnement: 31 × 2.50 = 77.50
        // Total: 263.50
        Assert.Equal(2, settlement.Lines.Count);
        Assert.Equal(263.50m, settlement.TotalAmount.Amount);
    }

    // --- Metadata ---

    [Fact]
    public void Calculate_SetsCorrectMetadata()
    {
        var ts = CreateJanuaryTimeSeries();
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.10m);

        var settlement = SettlementCalculator.Calculate(ts, supply, [nettarif], Array.Empty<SpotPrice>());

        Assert.Equal(MpId, settlement.MeteringPointId);
        Assert.Equal(supply.Id, settlement.SupplyId);
        Assert.Equal(ts.Id, settlement.TimeSeriesId);
        Assert.Equal(1, settlement.TimeSeriesVersion);
        Assert.Equal(Jan1, settlement.SettlementPeriod.Start);
        Assert.Equal(Feb1, settlement.SettlementPeriod.End);
        Assert.Equal(SettlementStatus.Calculated, settlement.Status);
        Assert.False(settlement.IsCorrection);
    }

    [Fact]
    public void Calculate_TotalEnergyMatchesTimeSeries()
    {
        var ts = CreateJanuaryTimeSeries(kwhPerHour: 3.5m);
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.10m);

        var settlement = SettlementCalculator.Calculate(ts, supply, [nettarif], Array.Empty<SpotPrice>());

        // 744 hours × 3.5 kWh = 2604.0
        Assert.Equal(2604.000m, settlement.TotalEnergy.Value);
    }

    // --- Time-varying tariffs ---

    [Fact]
    public void Calculate_TimeVaryingTariff_UsesCorrectPricePerHour()
    {
        // Create a short time series: just 4 hours
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 1, 1, 4, 0, 0, TimeSpan.Zero);
        var period = Period.Create(start, end);

        var ts = TimeSeries.Create(MpId, period, Resolution.PT1H, 1);
        ts.AddObservation(start.AddHours(0), EnergyQuantity.Create(1.0m), QuantityQuality.Measured); // hour 0
        ts.AddObservation(start.AddHours(1), EnergyQuantity.Create(1.0m), QuantityQuality.Measured); // hour 1
        ts.AddObservation(start.AddHours(2), EnergyQuantity.Create(1.0m), QuantityQuality.Measured); // hour 2
        ts.AddObservation(start.AddHours(3), EnergyQuantity.Create(1.0m), QuantityQuality.Measured); // hour 3

        // Price varies: 0.10 for hours 0-1, 0.30 for hours 2-3
        var pris = Price.Create("TV-001", GlnNumber.Create("5790001330552"),
            PriceType.Tarif, "Tidsvarieret tarif",
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        pris.AddPricePoint(start.AddHours(0), 0.10m);
        pris.AddPricePoint(start.AddHours(2), 0.30m);
        var priceLink = new PriceWithPoints(pris);

        var supply = CreateSupply();
        var settlement = SettlementCalculator.Calculate(ts, supply, [priceLink], Array.Empty<SpotPrice>());

        // 2 × 1.0 × 0.10 + 2 × 1.0 × 0.30 = 0.20 + 0.60 = 0.80
        Assert.Equal(0.80m, settlement.TotalAmount.Amount);
    }

    // --- Edge cases ---

    [Fact]
    public void Calculate_EmptyTimeSeries_Throws()
    {
        var ts = TimeSeries.Create(MpId, January, Resolution.PT1H, 1);
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        Assert.Throws<InvalidOperationException>(() =>
            SettlementCalculator.Calculate(ts, supply, [nettarif], Array.Empty<SpotPrice>()));
    }

    [Fact]
    public void Calculate_NoPrices_ZeroTotal()
    {
        var ts = CreateJanuaryTimeSeries();
        var supply = CreateSupply();

        var settlement = SettlementCalculator.Calculate(ts, supply, [], Array.Empty<SpotPrice>());

        Assert.Equal(0m, settlement.TotalAmount.Amount);
        Assert.Empty(settlement.Lines);
    }

    [Fact]
    public void Calculate_GebyrPriceType_IsIgnored()
    {
        var ts = CreateJanuaryTimeSeries();
        var supply = CreateSupply();

        var pris = Price.Create("GB-001", GlnNumber.Create("5790001330552"),
            PriceType.Gebyr, "Tilslutningsgebyr",
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        pris.AddPricePoint(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), 500m);
        var fee = new PriceWithPoints(pris);

        var settlement = SettlementCalculator.Calculate(ts, supply, [fee], Array.Empty<SpotPrice>());

        Assert.Equal(0m, settlement.TotalAmount.Amount);
        Assert.Empty(settlement.Lines);
    }

    // --- Correction calculations ---

    [Fact]
    public void CalculateCorrection_ProducesDelta()
    {
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        // Original: 1.0 kWh/h for January
        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, supply, [nettarif], Array.Empty<SpotPrice>());
        // Original: 744 × 1.0 × 0.25 = 186.00

        // Corrected data: 1.5 kWh/h (50% more consumption)
        var correctedTs = CreateJanuaryTimeSeries(kwhPerHour: 1.5m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, originalSettlement, [nettarif], Array.Empty<SpotPrice>());

        Assert.True(correction.IsCorrection);
        Assert.Equal(originalSettlement.Id, correction.PreviousSettlementId);
        Assert.Equal(2, correction.TimeSeriesVersion);
        Assert.Equal(SettlementStatus.Calculated, correction.Status);

        // Delta: (744 × 1.5 × 0.25) - (744 × 1.0 × 0.25) = 279.00 - 186.00 = 93.00
        Assert.Equal(93.00m, correction.TotalAmount.Amount);

        // Delta energy: 744 × (1.5 - 1.0) = 372.0
        Assert.Equal(372.000m, correction.TotalEnergy.Value);
    }

    [Fact]
    public void CalculateCorrection_LowerConsumption_NegativeDelta()
    {
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        // Original: 2.0 kWh/h
        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 2.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, supply, [nettarif], Array.Empty<SpotPrice>());

        // Corrected: 1.5 kWh/h (lower — customer overpaid)
        var correctedTs = CreateJanuaryTimeSeries(kwhPerHour: 1.5m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, originalSettlement, [nettarif], Array.Empty<SpotPrice>());

        // Delta: (744 × 1.5 × 0.25) - (744 × 2.0 × 0.25) = 279.00 - 372.00 = -93.00
        Assert.Equal(-93.00m, correction.TotalAmount.Amount);
        Assert.Equal(-372.000m, correction.TotalEnergy.Value);
    }

    [Fact]
    public void CalculateCorrection_NoChange_EmptyLines()
    {
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);

        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, supply, [nettarif], Array.Empty<SpotPrice>());

        // Same data, new version — nothing changed
        var sameTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            sameTs, supply, originalSettlement, [nettarif], Array.Empty<SpotPrice>());

        Assert.True(correction.IsCorrection);
        Assert.Equal(0m, correction.TotalAmount.Amount);
        Assert.Empty(correction.Lines); // No delta → no lines
    }

    // --- Spot price (PT15M) with hourly time series ---

    [Fact]
    public void Calculate_SpotPricePT15M_AveragesQuarterHourlyPrices()
    {
        // Spot price with 15-minute resolution: 4 different prices per hour
        var spotPrice = Price.Create(
            "SPOT-DK1",
            GlnNumber.Create("5790000432752"),
            PriceType.Tarif,
            "Spotpris — DK1",
            Period.From(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            priceResolution: Resolution.PT15M,
            isPassThrough: true);

        // Add 4 quarter-hour prices for hour 0 (00:00-01:00):
        // 0.50, 0.60, 0.70, 0.80 → average = 0.65
        spotPrice.AddPricePoint(Jan1.AddMinutes(0), 0.50m);
        spotPrice.AddPricePoint(Jan1.AddMinutes(15), 0.60m);
        spotPrice.AddPricePoint(Jan1.AddMinutes(30), 0.70m);
        spotPrice.AddPricePoint(Jan1.AddMinutes(45), 0.80m);

        // Add 4 quarter-hour prices for hour 1 (01:00-02:00):
        // 0.40, 0.40, 0.40, 0.40 → average = 0.40
        spotPrice.AddPricePoint(Jan1.AddHours(1).AddMinutes(0), 0.40m);
        spotPrice.AddPricePoint(Jan1.AddHours(1).AddMinutes(15), 0.40m);
        spotPrice.AddPricePoint(Jan1.AddHours(1).AddMinutes(30), 0.40m);
        spotPrice.AddPricePoint(Jan1.AddHours(1).AddMinutes(45), 0.40m);

        var spotPriceWithPoints = new PriceWithPoints(spotPrice);

        // Create a 2-hour time series (PT1H) with 1 kWh per hour
        var ts = TimeSeries.Create(MpId, Period.Create(Jan1, Jan1.AddHours(2)), Resolution.PT1H, 1, "TX-SPOT");
        ts.AddObservation(Jan1, EnergyQuantity.Create(1.0m), QuantityQuality.Measured);
        ts.AddObservation(Jan1.AddHours(1), EnergyQuantity.Create(1.0m), QuantityQuality.Measured);

        var supply = CreateSupply();
        var settlement = SettlementCalculator.Calculate(ts, supply, new[] { spotPriceWithPoints }, Array.Empty<SpotPrice>());

        Assert.Single(settlement.Lines);
        var spotLine = settlement.Lines.First();

        // Hour 0: 1.0 kWh × 0.65 = 0.65 DKK
        // Hour 1: 1.0 kWh × 0.40 = 0.40 DKK
        // Total: 1.05 DKK
        Assert.Equal(1.05m, spotLine.Amount.Amount);
        Assert.Equal(2.0m, spotLine.Quantity.Value);
    }

    [Fact]
    public void CalculateCorrection_MultiplePrices_CorrectDeltas()
    {
        var supply = CreateSupply();
        var nettarif = CreateFlatTariff("NT-001", "Nettarif", 0.25m);
        var systemtarif = CreateFlatTariff("ST-001", "Systemtarif", 0.054m);
        var prices = new List<PriceWithPoints> { nettarif, systemtarif };

        var originalTs = CreateJanuaryTimeSeries(kwhPerHour: 1.0m, version: 1);
        var originalSettlement = SettlementCalculator.Calculate(originalTs, supply, prices, Array.Empty<SpotPrice>());

        // 10% more consumption
        var correctedTs = CreateJanuaryTimeSeries(kwhPerHour: 1.1m, version: 2);

        var correction = SettlementCalculator.CalculateCorrection(
            correctedTs, supply, originalSettlement, prices, Array.Empty<SpotPrice>());

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
