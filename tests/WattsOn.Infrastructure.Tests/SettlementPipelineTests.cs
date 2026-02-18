using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Infrastructure.Tests;

/// <summary>
/// End-to-end integration tests for the settlement pipeline.
/// Uses a real PostgreSQL + TimescaleDB via Testcontainers.
/// </summary>
[Collection("Database")]
public class SettlementPipelineTests
{
    private readonly DatabaseFixture _fixture;

    public SettlementPipelineTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FullPipeline_IngestTimeSeries_CalculateSettlement_ConfirmInvoice_DetectCorrection()
    {
        await using var db = await _fixture.CreateCleanContext();

        // === Setup: Actor, Customer, MeteringPoint, Supply ===
        var actor = Actor.Create(GlnNumber.Create("5790001330552"), "WattsOn Energy A/S", ActorRole.Supplier, isOwn: true);
        db.Actors.Add(actor);

        var customer = Customer.CreatePerson("Hans Jensen", CprNumber.Create("0101901234"));
        db.Customers.Add(customer);

        var mp = MeteringPoint.Create(
            Gsrn.Create("571313100000000001"),
            MeteringPointType.Forbrug,
            MeteringPointCategory.Fysisk,
            SettlementMethod.Flex,
            Resolution.PT1H,
            "DK1",
            GlnNumber.Create("5790000610099"));
        db.MeteringPoints.Add(mp);

        var supply = Supply.Create(mp.Id, customer.Id, actor.Id,
            Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        db.Supplies.Add(supply);

        // === Setup: Price (flat tariff 0.50 DKK/kWh) ===
        var price = Price.Create(
            "NET-TARIF", GlnNumber.Create("5790000610099"), PriceType.Tarif,
            "Nettarif", Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        price.AddPricePoint(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 0.50m);
        db.Prices.Add(price);

        var link = PriceLink.Create(mp.Id, price.Id,
            Period.From(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        db.PriceLinks.Add(link);

        await db.SaveChangesAsync();

        // === Step 1: Ingest time series (24 hours, 1 kWh each = 24 kWh) ===
        var start = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 1, 16, 0, 0, 0, TimeSpan.Zero);
        var ts = TimeSeries.Create(mp.Id, Period.Create(start, end), Resolution.PT1H, version: 1);
        for (int h = 0; h < 24; h++)
            ts.AddObservation(start.AddHours(h), EnergyQuantity.Create(1.0m), QuantityQuality.Measured);
        db.TimeSeriesCollection.Add(ts);
        await db.SaveChangesAsync();

        // === Step 2: Calculate settlement ===
        var priceLinks = await db.PriceLinks
            .Include(pl => pl.Price).ThenInclude(p => p.PricePoints)
            .Where(pl => pl.MeteringPointId == mp.Id)
            .ToListAsync();
        var activePrices = priceLinks.Select(pl => new PriceWithPoints(pl.Price)).ToList();

        var settlement = SettlementCalculator.Calculate(ts, supply, activePrices);
        db.Settlements.Add(settlement);
        await db.SaveChangesAsync();

        // Verify settlement
        Assert.Equal(24m, settlement.TotalEnergy.Value);
        Assert.Equal(12.00m, settlement.TotalAmount.Amount); // 24 kWh × 0.50 DKK
        Assert.Equal(SettlementStatus.Calculated, settlement.Status);
        Assert.False(settlement.IsCorrection);

        // === Step 3: Confirm invoicing ===
        settlement.MarkInvoiced("INV-2026-001");
        await db.SaveChangesAsync();

        Assert.Equal(SettlementStatus.Invoiced, settlement.Status);
        Assert.Equal("INV-2026-001", settlement.ExternalInvoiceReference);

        // === Step 4: Ingest corrected time series (1.5 kWh per hour = 36 kWh) ===
        var correctedTs = TimeSeries.Create(mp.Id, Period.Create(start, end), Resolution.PT1H, version: 2);
        for (int h = 0; h < 24; h++)
            correctedTs.AddObservation(start.AddHours(h), EnergyQuantity.Create(1.5m), QuantityQuality.Measured);
        db.TimeSeriesCollection.Add(correctedTs);
        // Mark old as not latest
        ts.Supersede();
        await db.SaveChangesAsync();

        // === Step 5: Calculate correction (delta settlement) ===
        settlement.MarkAdjusted();
        var correction = SettlementCalculator.CalculateCorrection(correctedTs, supply, settlement, activePrices);
        db.Settlements.Add(correction);
        await db.SaveChangesAsync();

        // Verify correction
        Assert.True(correction.IsCorrection);
        Assert.Equal(settlement.Id, correction.PreviousSettlementId);
        Assert.Equal(12m, correction.TotalEnergy.Value);    // 36 - 24 = 12 kWh delta
        Assert.Equal(6.00m, correction.TotalAmount.Amount);  // 12 kWh × 0.50 DKK delta
        Assert.Equal(SettlementStatus.Calculated, correction.Status);

        // Original marked as adjusted
        Assert.Equal(SettlementStatus.Adjusted, settlement.Status);

        // === Verify database state ===
        var allSettlements = await db.Settlements.ToListAsync();
        Assert.Equal(2, allSettlements.Count);
        Assert.Single(allSettlements, s => !s.IsCorrection);
        Assert.Single(allSettlements, s => s.IsCorrection);
    }

    [Fact]
    public async Task SpotPrices_CanPersistAndQuery()
    {
        await using var db = await _fixture.CreateCleanContext();

        var spot1 = SpotPrice.Create(
            new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero),
            "DK1", 690.50m, 92.50m);

        var spot2 = SpotPrice.Create(
            new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 1, 15, 13, 0, 0, TimeSpan.Zero),
            "DK2", 685.25m, 91.80m);

        db.SpotPrices.AddRange(spot1, spot2);
        await db.SaveChangesAsync();

        var dk1Prices = await db.SpotPrices.Where(sp => sp.PriceArea == "DK1").ToListAsync();
        Assert.Single(dk1Prices);
        Assert.Equal(690.50m, dk1Prices[0].SpotPriceDkkPerMwh);
        Assert.Equal(0.69050m, dk1Prices[0].SpotPriceDkkPerKwh);

        var allPrices = await db.SpotPrices.CountAsync();
        Assert.Equal(2, allPrices);
    }

    [Fact]
    public async Task BrsProcess_PersistsWithTransitions()
    {
        await using var db = await _fixture.CreateCleanContext();

        var mp = MeteringPoint.Create(
            Gsrn.Create("571313100000000002"),
            MeteringPointType.Forbrug,
            MeteringPointCategory.Fysisk,
            SettlementMethod.Flex,
            Resolution.PT1H,
            "DK1",
            GlnNumber.Create("5790000610099"));
        db.MeteringPoints.Add(mp);

        var process = Brs001Handler.InitiateSupplierChange(
            Gsrn.Create("571313100000000002"),
            DateTimeOffset.UtcNow.AddDays(14),
            cprNumber: "0101901234",
            cvrNumber: null,
            GlnNumber.Create("5790000610099"));

        db.Processes.Add(process);
        await db.SaveChangesAsync();

        // Reload from DB
        var loaded = await db.Processes
            .Include(p => p.Transitions)
            .FirstAsync(p => p.Id == process.Id);

        Assert.Equal(ProcessType.Leverandørskift, loaded.ProcessType);
        Assert.Equal(ProcessRole.Initiator, loaded.Role);
        Assert.Equal("571313100000000002", loaded.MeteringPointGsrn?.Value);
    }

    [Fact]
    public async Task Customer_PersistsWithAddress()
    {
        await using var db = await _fixture.CreateCleanContext();

        var customer = Customer.CreatePerson("Maria Nielsen", CprNumber.Create("1505851234"));
        customer.UpdateAddress(Address.Create("Vestergade", "12", "8000", "Aarhus C"));
        customer.UpdateContactInfo("maria@example.dk", "+4512345678");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var loaded = await db.Customers.FirstAsync(c => c.Id == customer.Id);
        Assert.Equal("Maria Nielsen", loaded.Name);
        Assert.Equal("8000", loaded.Address!.PostCode);
        Assert.Equal("maria@example.dk", loaded.Email);
    }

    [Fact]
    public async Task TimeSeries_VersioningWorks()
    {
        await using var db = await _fixture.CreateCleanContext();

        var mp = MeteringPoint.Create(
            Gsrn.Create("571313100000000003"),
            MeteringPointType.Forbrug,
            MeteringPointCategory.Fysisk,
            SettlementMethod.Flex,
            Resolution.PT1H,
            "DK1",
            GlnNumber.Create("5790000610099"));
        db.MeteringPoints.Add(mp);

        var start = new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero);

        // Version 1
        var ts1 = TimeSeries.Create(mp.Id, Period.Create(start, end), Resolution.PT1H, version: 1);
        for (int h = 0; h < 24; h++) ts1.AddObservation(start.AddHours(h), EnergyQuantity.Create(1.0m), QuantityQuality.Measured);
        db.TimeSeriesCollection.Add(ts1);
        await db.SaveChangesAsync();

        Assert.True(ts1.IsLatest);

        // Version 2 — supersedes version 1
        var ts2 = TimeSeries.Create(mp.Id, Period.Create(start, end), Resolution.PT1H, version: 2);
        for (int h = 0; h < 24; h++) ts2.AddObservation(start.AddHours(h), EnergyQuantity.Create(1.5m), QuantityQuality.Measured);
        ts1.Supersede();
        db.TimeSeriesCollection.Add(ts2);
        await db.SaveChangesAsync();

        Assert.False(ts1.IsLatest);
        Assert.True(ts2.IsLatest);

        var latest = await db.TimeSeriesCollection
            .Include(t => t.Observations)
            .Where(t => t.MeteringPointId == mp.Id && t.IsLatest)
            .FirstAsync();

        Assert.Equal(2, latest.Version);
        Assert.Equal(24, latest.Observations.Count);
        Assert.Equal(1.5m, latest.Observations.First().Quantity.Value);
    }
}
