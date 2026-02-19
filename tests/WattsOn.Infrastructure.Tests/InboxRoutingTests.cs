using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;
using WattsOn.Worker;

namespace WattsOn.Infrastructure.Tests;

[Collection("Database")]
public class InboxRoutingTests
{
    private readonly DatabaseFixture _fixture;

    public InboxRoutingTests(DatabaseFixture fixture) => _fixture = fixture;

    /// <summary>
    /// Create a worker instance for calling RouteMessage.
    /// We only need the worker to call RouteMessage, not ExecuteAsync,
    /// so the scope factory is a dummy — RouteMessage takes DbContext directly.
    /// </summary>
    private InboxPollingWorker CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddDbContext<WattsOnDbContext>(o => o.UseNpgsql(_fixture.ConnectionString));
        var sp = services.BuildServiceProvider();
        return new InboxPollingWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InboxPollingWorker>.Instance);
    }

    private static InboxMessage CreateInbox(string businessProcess, string documentType, object payload)
    {
        return InboxMessage.Create(
            messageId: $"TEST-{Guid.NewGuid():N}",
            documentType: documentType,
            senderGln: "5790000610099",
            receiverGln: "5790001330552",
            rawPayload: JsonSerializer.Serialize(payload),
            businessProcess: businessProcess);
    }

    /// <summary>
    /// Standard metering point setup: SupplierIdentity + Customer + MeteringPoint + Supply.
    /// </summary>
    private static async Task<(MeteringPoint Mp, Supply Supply, Customer Customer)> SetupMeteringPoint(
        WattsOnDbContext db, string gsrn = "571313100000000001")
    {
        var identity = SupplierIdentity.Create(GlnNumber.Create("5790001330552"), "Test Supplier");
        db.SupplierIdentities.Add(identity);

        var customer = Customer.CreatePerson("Test Customer", CprNumber.Create("0101901234"), identity.Id);
        db.Customers.Add(customer);

        var mp = MeteringPoint.Create(
            Gsrn.Create(gsrn),
            MeteringPointType.Forbrug,
            MeteringPointCategory.Fysisk,
            SettlementMethod.Flex,
            Resolution.PT1H,
            "DK1",
            GlnNumber.Create("5790000610099"));
        db.MeteringPoints.Add(mp);

        var supply = Supply.Create(mp.Id, customer.Id,
            Period.From(DateTimeOffset.UtcNow.AddDays(-30)));
        db.Supplies.Add(supply);

        await db.SaveChangesAsync();
        return (mp, supply, customer);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-021 — Metered data creates TimeSeries
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS021_MeteredData_CreatesTimeSeries()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();
        var (mp, _, _) = await SetupMeteringPoint(db);

        var message = CreateInbox("BRS-021", "RSM-012", new
        {
            gsrn = "571313100000000001",
            periodStart = "2026-01-15T00:00:00Z",
            periodEnd = "2026-01-16T00:00:00Z",
            resolution = "PT1H",
            transactionId = "TX-001",
            observations = Enumerable.Range(0, 24).Select(h => new
            {
                timestamp = new DateTimeOffset(2026, 1, 15, h, 0, 0, TimeSpan.Zero),
                kwh = 1.5m,
                quality = (string?)null
            })
        });

        await worker.RouteMessage(db, message, CancellationToken.None);
        await db.SaveChangesAsync();

        var ts = await db.TimeSeriesCollection
            .Include(t => t.Observations)
            .Where(t => t.MeteringPointId == mp.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(ts);
        Assert.Equal(1, ts.Version);
        Assert.True(ts.IsLatest);
        Assert.Equal(24, ts.Observations.Count);
        Assert.Equal(36.0m, ts.TotalEnergy.Value); // 24 × 1.5
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-021 — Correction supersedes existing TimeSeries
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS021_CorrectedData_SupersedesExisting()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();
        var (mp, _, _) = await SetupMeteringPoint(db);

        // First submission
        var msg1 = CreateInbox("BRS-021", "RSM-012", new
        {
            gsrn = "571313100000000001",
            periodStart = "2026-01-15T00:00:00Z",
            periodEnd = "2026-01-16T00:00:00Z",
            resolution = "PT1H",
            observations = Enumerable.Range(0, 24).Select(h => new
            {
                timestamp = new DateTimeOffset(2026, 1, 15, h, 0, 0, TimeSpan.Zero),
                kwh = 1.0m,
                quality = (string?)null
            })
        });
        await worker.RouteMessage(db, msg1, CancellationToken.None);
        await db.SaveChangesAsync();

        // Second submission (correction)
        var msg2 = CreateInbox("BRS-021", "RSM-012", new
        {
            gsrn = "571313100000000001",
            periodStart = "2026-01-15T00:00:00Z",
            periodEnd = "2026-01-16T00:00:00Z",
            resolution = "PT1H",
            observations = Enumerable.Range(0, 24).Select(h => new
            {
                timestamp = new DateTimeOffset(2026, 1, 15, h, 0, 0, TimeSpan.Zero),
                kwh = 2.0m,
                quality = (string?)null
            })
        });
        await worker.RouteMessage(db, msg2, CancellationToken.None);
        await db.SaveChangesAsync();

        var all = await db.TimeSeriesCollection
            .Where(t => t.MeteringPointId == mp.Id)
            .OrderBy(t => t.Version)
            .ToListAsync();

        Assert.Equal(2, all.Count);
        Assert.False(all[0].IsLatest);
        Assert.True(all[1].IsLatest);
        Assert.Equal(2, all[1].Version);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-006 — Master data update changes metering point
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS006_MasterDataUpdate_ChangesMeteringPoint()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();
        var (mp, _, _) = await SetupMeteringPoint(db);

        var message = CreateInbox("BRS-006", "RSM-022", new
        {
            gsrn = "571313100000000001",
            gridArea = "DK2",
            gridCompanyGln = "5790000610099",
            connectionState = "Afbrudt"
        });

        await worker.RouteMessage(db, message, CancellationToken.None);
        await db.SaveChangesAsync();

        var updated = await db.MeteringPoints.FirstAsync(m => m.Id == mp.Id);
        Assert.Equal("DK2", updated.GridArea);
        Assert.Equal(ConnectionState.Afbrudt, updated.ConnectionState);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-031 — D18 price info creates/updates price
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS031_D18_PriceInfo_CreatesPrice()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();

        var message = CreateInbox("BRS-031", "RSM-033", new
        {
            businessReason = "D18",
            chargeId = "INT-TARIF",
            ownerGln = "5790000610099",
            priceType = "Tarif",
            description = "Integration Test Tariff",
            effectiveDate = "2026-01-01T00:00:00Z",
            resolution = "PT1H",
            vatExempt = false,
            isTax = false,
            isPassThrough = true
        });

        await worker.RouteMessage(db, message, CancellationToken.None);
        await db.SaveChangesAsync();

        var price = await db.Prices.FirstOrDefaultAsync(p => p.ChargeId == "INT-TARIF");
        Assert.NotNull(price);
        Assert.Equal("Integration Test Tariff", price.Description);
        Assert.Equal(PriceType.Tarif, price.Type);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-031 — D08 price series adds price points
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS031_D08_PriceSeries_AddsPricePoints()
    {
        await using var setupDb = await _fixture.CreateCleanContext();

        // First create the price via D18 (setup phase)
        var worker1 = CreateWorker();
        var d18Msg = CreateInbox("BRS-031", "RSM-033", new
        {
            businessReason = "D18",
            chargeId = "POINT-TEST",
            ownerGln = "5790000610099",
            priceType = "Tarif",
            description = "Price Points Test",
            effectiveDate = "2026-01-01T00:00:00Z",
            resolution = "PT1H",
            vatExempt = false,
            isTax = false,
            isPassThrough = true
        });
        await worker1.RouteMessage(setupDb, d18Msg, CancellationToken.None);
        await setupDb.SaveChangesAsync();

        // Fresh context for the D08 routing (mimics production: new scope per poll cycle)
        await using var db = _fixture.CreateContext();
        var worker = CreateWorker();

        var d08Msg = CreateInbox("BRS-031", "RSM-034", new
        {
            businessReason = "D08",
            chargeId = "POINT-TEST",
            ownerGln = "5790000610099",
            startDate = "2026-01-15T00:00:00Z",
            endDate = "2026-01-16T00:00:00Z",
            points = Enumerable.Range(0, 24).Select(h => new
            {
                timestamp = new DateTimeOffset(2026, 1, 15, h, 0, 0, TimeSpan.Zero),
                price = 0.50m + (h >= 17 && h < 21 ? 1.0m : 0m) // Peak hours
            })
        });
        await worker.RouteMessage(db, d08Msg, CancellationToken.None);
        await db.SaveChangesAsync();

        var price = await db.Prices
            .Include(p => p.PricePoints)
            .FirstAsync(p => p.ChargeId == "POINT-TEST");
        Assert.Equal(24, price.PricePoints.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-031 — D17 price link links price to metering point
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS031_D17_PriceLink_LinksPriceToMeteringPoint()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();
        var (mp, _, _) = await SetupMeteringPoint(db);

        // Create price first
        var d18Msg = CreateInbox("BRS-031", "RSM-033", new
        {
            businessReason = "D18",
            chargeId = "LINK-TEST",
            ownerGln = "5790000610099",
            priceType = "Tarif",
            description = "Link Test",
            effectiveDate = "2026-01-01T00:00:00Z",
            resolution = "PT1H",
            vatExempt = false,
            isTax = false,
            isPassThrough = true
        });
        await worker.RouteMessage(db, d18Msg, CancellationToken.None);
        await db.SaveChangesAsync();

        // Link it
        var d17Msg = CreateInbox("BRS-031", "RSM-030", new
        {
            businessReason = "D17",
            gsrn = "571313100000000001",
            chargeId = "LINK-TEST",
            ownerGln = "5790000610099",
            linkStart = "2026-01-01T00:00:00Z"
        });
        await worker.RouteMessage(db, d17Msg, CancellationToken.None);
        await db.SaveChangesAsync();

        var link = await db.PriceLinks.FirstOrDefaultAsync(pl => pl.MeteringPointId == mp.Id);
        Assert.NotNull(link);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-023 — Aggregated time series stored
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS023_AggregatedTimeSeries_Stored()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();

        var message = CreateInbox("BRS-023", "RSM-014", new
        {
            gridArea = "DK1",
            businessReason = "D03",
            meteringPointType = "E17",
            settlementMethod = "E02",
            periodStart = "2026-01-15T00:00:00Z",
            periodEnd = "2026-01-16T00:00:00Z",
            resolution = "PT1H",
            qualityStatus = "Measured",
            observations = Enumerable.Range(0, 24).Select(h => new
            {
                timestamp = new DateTimeOffset(2026, 1, 15, h, 0, 0, TimeSpan.Zero),
                kwh = 150.0m
            })
        });

        await worker.RouteMessage(db, message, CancellationToken.None);
        await db.SaveChangesAsync();

        var agg = await db.AggregatedTimeSeriesCollection
            .Include(a => a.Observations)
            .FirstOrDefaultAsync();

        Assert.NotNull(agg);
        Assert.Equal("DK1", agg.GridArea);
        Assert.Equal("D03", agg.BusinessReason);
        Assert.Equal(24, agg.Observations.Count);
        Assert.Equal(3600.0m, agg.TotalEnergyKwh); // 24 × 150
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-027 — Wholesale settlement stored
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS027_WholesaleSettlement_Stored()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();

        var message = CreateInbox("BRS-027", "RSM-019", new
        {
            gridArea = "DK1",
            businessReason = "D05",
            periodStart = "2026-01-01T00:00:00Z",
            periodEnd = "2026-02-01T00:00:00Z",
            resolution = "PT1H",
            lines = new[]
            {
                new { chargeId = "NET-TARIF", chargeType = "Tarif", ownerGln = "5790000610099",
                      energyKwh = 1000.0m, amountDkk = 500.0m, description = "Nettarif" },
                new { chargeId = "SYS-TARIF", chargeType = "Tarif", ownerGln = "5790000610099",
                      energyKwh = 1000.0m, amountDkk = 54.0m, description = "Systemtarif" }
            }
        });

        await worker.RouteMessage(db, message, CancellationToken.None);
        await db.SaveChangesAsync();

        var ws = await db.WholesaleSettlements
            .Include(w => w.Lines)
            .FirstOrDefaultAsync();

        Assert.NotNull(ws);
        Assert.Equal("DK1", ws.GridArea);
        Assert.Equal(2, ws.Lines.Count);
        Assert.Equal(554.0m, ws.TotalAmountDkk);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-002 — Confirmation ends supply
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS002_Confirmation_EndsSupply()
    {
        await using var setupDb = await _fixture.CreateCleanContext();
        var (mp, supply, _) = await SetupMeteringPoint(setupDb);

        // Create an active BRS-002 process (simulating that we already initiated)
        var process = BrsProcess.Create(
            ProcessType.Supplyophør,
            ProcessRole.Initiator,
            "Submitted",
            Gsrn.Create("571313100000000001"),
            DateTimeOffset.UtcNow.AddDays(7));
        process.MarkSubmitted("TX-002");
        setupDb.Processes.Add(process);
        await setupDb.SaveChangesAsync();

        var processId = process.Id;
        var supplyId = supply.Id;

        // Fresh context for routing (mimics production: new scope per poll cycle)
        await using var db = _fixture.CreateContext();
        var worker = CreateWorker();

        // DataHub confirms
        var message = CreateInbox("BRS-002", "RSM-005", new
        {
            gsrn = "571313100000000001",
            actualEndDate = DateTimeOffset.UtcNow.AddDays(7).ToString("o"),
            rejected = false
        });

        await worker.RouteMessage(db, message, CancellationToken.None);
        await db.SaveChangesAsync();

        var updatedProcess = await db.Processes.FirstAsync(p => p.Id == processId);
        Assert.Equal(ProcessStatus.Completed, updatedProcess.Status);

        var updatedSupply = await db.Supplies.FirstAsync(s => s.Id == supplyId);
        Assert.NotNull(updatedSupply.SupplyPeriod.End);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BRS-015 — Confirmation completes process
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS015_Confirmation_CompletesProcess()
    {
        await using var setupDb = await _fixture.CreateCleanContext();
        var (mp, _, _) = await SetupMeteringPoint(setupDb);

        var process = BrsProcess.Create(
            ProcessType.CustomerStamdataOpdatering,
            ProcessRole.Initiator,
            "Submitted",
            Gsrn.Create("571313100000000001"),
            DateTimeOffset.UtcNow);
        process.MarkSubmitted("TX-015");
        setupDb.Processes.Add(process);
        await setupDb.SaveChangesAsync();

        var processId = process.Id;

        // Fresh context for routing (mimics production: new scope per poll cycle)
        await using var db = _fixture.CreateContext();
        var worker = CreateWorker();

        var message = CreateInbox("BRS-015", "RSM-027", new
        {
            gsrn = "571313100000000001",
            rejected = false
        });

        await worker.RouteMessage(db, message, CancellationToken.None);
        await db.SaveChangesAsync();

        var updated = await db.Processes.FirstAsync(p => p.Id == processId);
        Assert.Equal(ProcessStatus.Completed, updated.Status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edge case: Unknown BRS process handled gracefully
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownBRS_HandledGracefully()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();

        var message = CreateInbox("BRS-999", "RSM-999", new { foo = "bar" });

        // Should not throw
        await worker.RouteMessage(db, message, CancellationToken.None);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Edge case: BRS-021 missing GSRN is skipped gracefully
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BRS021_MissingGsrn_SkipsGracefully()
    {
        await using var db = await _fixture.CreateCleanContext();
        var worker = CreateWorker();

        var message = CreateInbox("BRS-021", "RSM-012", new
        {
            periodStart = "2026-01-15T00:00:00Z",
            periodEnd = "2026-01-16T00:00:00Z",
            resolution = "PT1H",
            observations = new[] { new { timestamp = "2026-01-15T00:00:00Z", kwh = 1.0m } }
        });

        // Should not throw, and no TimeSeries should be created
        await worker.RouteMessage(db, message, CancellationToken.None);

        var count = await db.TimeSeriesCollection.CountAsync();
        Assert.Equal(0, count);
    }
}
