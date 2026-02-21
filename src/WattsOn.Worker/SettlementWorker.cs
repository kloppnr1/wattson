using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Watches for new time series that haven't been settled yet, and triggers
/// automatic settlement calculation. Also detects corrections for already-invoiced settlements.
///
/// Flow:
/// 1. Find latest time series that have no matching Settlement
/// 2. For each: look up active price links, run SettlementCalculator
/// 3. If a prior invoiced settlement exists for same metering_point + period → correction flow
/// 4. Save the resulting Settlement(er)
/// </summary>
public class SettlementWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SettlementWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30);

    public SettlementWorker(IServiceScopeFactory scopeFactory, ILogger<SettlementWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SettlementWorker starting — polling every {Interval}s", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessUnsettledTimeSeries(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in settlement processing");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task ProcessUnsettledTimeSeries(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        // Find latest time series that don't have a matching settlement.
        // Also exclude time series where a migrated settlement already covers the same
        // metering point + period (migrated settlements reference placeholder time series,
        // not the actual imported observations).
        var unsettled = await db.TimeSeriesCollection
            .Include(ts => ts.Observations)
            .Where(ts => ts.IsLatest)
            .Where(ts => !db.Settlements.Any(a =>
                a.TimeSeriesId == ts.Id && a.TimeSeriesVersion == ts.Version))
            .Where(ts => !db.Settlements.Any(a =>
                a.MeteringPointId == ts.MeteringPointId
                && a.SettlementPeriod.Start == ts.Period.Start
                && a.SettlementPeriod.End == ts.Period.End
                && (a.Status == SettlementStatus.Invoiced
                    || a.Status == SettlementStatus.Migrated
                    || a.Status == SettlementStatus.Calculated
                    || a.Status == SettlementStatus.Adjusted)))
            .OrderBy(ts => ts.ReceivedAt)
            .Take(10)
            .ToListAsync(ct);

        if (unsettled.Count == 0) return;

        _logger.LogInformation("Found {Count} unsettled time series", unsettled.Count);

        foreach (var timeSeries in unsettled)
        {
            try
            {
                await SettleTimeSeries(db, timeSeries, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to settle time series {TimeSeriesId} v{Version}",
                    timeSeries.Id, timeSeries.Version);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SettleTimeSeries(WattsOnDbContext db, TimeSeries timeSeries, CancellationToken ct)
    {
        if (timeSeries.Observations.Count == 0)
        {
            _logger.LogDebug("Skipping time series {Id} — no observations", timeSeries.Id);
            return;
        }

        // Find the active supply for this metering_point at the start of the settlement period
        var supply = await db.Supplies
            .Include(l => l.Customer)
            .Where(l => l.MeteringPointId == timeSeries.MeteringPointId)
            .Where(l => l.SupplyPeriod.Start <= timeSeries.Period.Start)
            .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > timeSeries.Period.Start)
            .FirstOrDefaultAsync(ct);

        if (supply is null)
        {
            _logger.LogWarning("No active supply for metering_point {MpId} at {PeriodStart} — skipping",
                timeSeries.MeteringPointId, timeSeries.Period.Start);
            return;
        }

        // Get DataHub charge links for this metering point
        var priceLinks = await db.PriceLinks
            .Include(pt => pt.Price)
                .ThenInclude(p => p.PricePoints)
            .Where(pt => pt.MeteringPointId == timeSeries.MeteringPointId)
            .Where(pt => pt.LinkPeriod.Start <= timeSeries.Period.Start)
            .Where(pt => pt.LinkPeriod.End == null || pt.LinkPeriod.End > timeSeries.Period.Start)
            .ToListAsync(ct);

        var datahubPrices = priceLinks
            .Select(pt => new PriceWithPoints(pt.Price))
            .ToList();

        // Get metering point for grid area
        var meteringPoint = await db.MeteringPoints
            .FirstOrDefaultAsync(m => m.Id == timeSeries.MeteringPointId, ct);
        if (meteringPoint is null) return;

        // Get spot prices for the period + grid area
        var periodEnd = timeSeries.Period.End ?? timeSeries.Period.Start.AddMonths(1);
        var spotPrices = await db.SpotPrices
            .Where(sp => sp.PriceArea == meteringPoint.GridArea)
            .Where(sp => sp.Timestamp >= timeSeries.Period.Start && sp.Timestamp < periodEnd)
            .OrderBy(sp => sp.Timestamp)
            .ToListAsync(ct);

        // Get supplier margin for the period via supply → active product → margin
        var activeProductPeriod = await db.SupplyProductPeriods
            .Where(pp => pp.SupplyId == supply.Id)
            .Where(pp => pp.Period.Start <= timeSeries.Period.Start)
            .Where(pp => pp.Period.End == null || pp.Period.End > timeSeries.Period.Start)
            .FirstOrDefaultAsync(ct);

        SupplierMargin? activeMargin = null;
        var pricingModel = PricingModel.SpotAddon;

        if (activeProductPeriod is not null)
        {
            var product = await db.SupplierProducts.FindAsync(activeProductPeriod.SupplierProductId, ct);
            pricingModel = product?.PricingModel ?? PricingModel.SpotAddon;

            // Find the active margin rate: latest ValidFrom <= settlement period start
            activeMargin = await db.SupplierMargins
                .Where(m => m.SupplierProductId == activeProductPeriod.SupplierProductId)
                .Where(m => m.ValidFrom <= timeSeries.Period.Start)
                .OrderByDescending(m => m.ValidFrom)
                .FirstOrDefaultAsync(ct);
        }
        else
        {
            _logger.LogWarning("No active product on supply {SupplyId} at {PeriodStart} — settlement will have no margin",
                supply.Id, timeSeries.Period.Start);
        }

        // Validate all price sources
        var validationIssues = SettlementValidator.Validate(
            datahubPrices, spotPrices, activeMargin, pricingModel,
            timeSeries.Period.Start, periodEnd, timeSeries.Resolution);

        if (validationIssues.Count > 0)
        {
            _logger.LogWarning(
                "Settlement blocked for metering_point {MpId}, period {Start}: {Issues}",
                timeSeries.MeteringPointId, timeSeries.Period.Start,
                string.Join("; ", validationIssues));

            await PersistIssue(db, timeSeries, SettlementIssue.CreateMissingPrices(
                timeSeries.MeteringPointId, timeSeries.Id, timeSeries.Version,
                timeSeries.Period, validationIssues), ct);
            return;
        }

        // Resolve any prior open issues
        var priorIssues = await db.SettlementIssues
            .Where(i => i.MeteringPointId == timeSeries.MeteringPointId
                && i.TimeSeriesId == timeSeries.Id
                && i.Status == SettlementIssueStatus.Open)
            .ToListAsync(ct);
        foreach (var issue in priorIssues)
            issue.Resolve();

        // Check if there's already an invoiced settlement for this period
        var existingInvoicedSettlement = await db.Settlements
            .Include(a => a.Lines)
            .Where(a => a.MeteringPointId == timeSeries.MeteringPointId)
            .Where(a => a.SettlementPeriod.Start == timeSeries.Period.Start)
            .Where(a => a.SettlementPeriod.End == timeSeries.Period.End)
            .Where(a => a.Status == SettlementStatus.Invoiced || a.Status == SettlementStatus.Migrated)
            .OrderByDescending(a => a.TimeSeriesVersion)
            .FirstOrDefaultAsync(ct);

        if (existingInvoicedSettlement is not null)
        {
            // Correction flow: calculate delta against the invoiced settlement
            _logger.LogInformation(
                "Correction detected for metering_point {MpId}, period {Start}-{End}. " +
                "Original settlement {OriginalId} (v{OriginalVersion}) → new v{NewVersion}",
                timeSeries.MeteringPointId,
                timeSeries.Period.Start,
                timeSeries.Period.End,
                existingInvoicedSettlement.Id,
                existingInvoicedSettlement.TimeSeriesVersion,
                timeSeries.Version);

            existingInvoicedSettlement.MarkAdjusted();

            var correction = SettlementCalculator.CalculateCorrection(
                timeSeries, supply, existingInvoicedSettlement, datahubPrices, spotPrices, activeMargin, pricingModel);

            db.Settlements.Add(correction);

            _logger.LogInformation(
                "Created correction settlement {CorrectionId}: delta {DeltaAmount} DKK, {DeltaEnergy} kWh",
                correction.Id,
                correction.TotalAmount.Amount,
                correction.TotalEnergy.Value);
        }
        else
        {
            // Normal flow: calculate new settlement
            var settlement = SettlementCalculator.Calculate(
                timeSeries, supply, datahubPrices, spotPrices, activeMargin, pricingModel);

            db.Settlements.Add(settlement);

            _logger.LogInformation(
                "Created settlement {SettlementId} for metering_point {MpId}: {Amount} DKK, {Energy} kWh",
                settlement.Id,
                timeSeries.MeteringPointId,
                settlement.TotalAmount.Amount,
                settlement.TotalEnergy.Value);
        }
    }

    /// <summary>
    /// Persist a settlement issue, avoiding duplicates for the same metering point + time series + version.
    /// </summary>
    private async Task PersistIssue(WattsOnDbContext db, TimeSeries timeSeries, SettlementIssue issue, CancellationToken ct)
    {
        var existing = await db.SettlementIssues
            .FirstOrDefaultAsync(i =>
                i.MeteringPointId == timeSeries.MeteringPointId
                && i.TimeSeriesId == timeSeries.Id
                && i.TimeSeriesVersion == timeSeries.Version
                && i.Status == SettlementIssueStatus.Open, ct);

        if (existing is not null)
        {
            // Update existing issue (details might have changed)
            return;
        }

        db.SettlementIssues.Add(issue);
        await db.SaveChangesAsync(ct);
    }
}
