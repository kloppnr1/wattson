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

        // Find latest time series that don't have a matching settlement
        var unsettled = await db.TimeSeriesCollection
            .Include(ts => ts.Observations)
            .Where(ts => ts.IsLatest)
            .Where(ts => !db.Settlements.Any(a =>
                a.TimeSeriesId == ts.Id && a.TimeSeriesVersion == ts.Version))
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

        // Get active price links for this metering_point in this period
        var priceLinks = await db.PriceLinks
            .Include(pt => pt.Price)
                .ThenInclude(p => p.PricePoints)
            .Where(pt => pt.MeteringPointId == timeSeries.MeteringPointId)
            .Where(pt => pt.LinkPeriod.Start <= timeSeries.Period.Start)
            .Where(pt => pt.LinkPeriod.End == null || pt.LinkPeriod.End > timeSeries.Period.Start)
            .ToListAsync(ct);

        var activePrices = priceLinks
            .Select(pt => new PriceWithPoints(pt.Price))
            .ToList();

        // Validate all required price elements are linked
        var missingElements = SettlementValidator.ValidatePriceCompleteness(activePrices);
        if (missingElements.Count > 0)
        {
            _logger.LogWarning(
                "Skipping settlement for metering_point {MpId}, period {Start}: missing price elements: {Missing}",
                timeSeries.MeteringPointId, timeSeries.Period.Start,
                string.Join(", ", missingElements));
            return;
        }

        // Validate price points exist for the period
        var coverageIssues = SettlementValidator.ValidatePricePointCoverage(
            activePrices, timeSeries.Period.Start, timeSeries.Period.End);
        if (coverageIssues.Count > 0)
        {
            _logger.LogWarning(
                "Skipping settlement for metering_point {MpId}, period {Start}: price point coverage issues: {Issues}",
                timeSeries.MeteringPointId, timeSeries.Period.Start,
                string.Join("; ", coverageIssues));
            return;
        }

        // Check if there's already an invoiced settlement for this period
        var existingInvoicedSettlement = await db.Settlements
            .Include(a => a.Lines)
            .Where(a => a.MeteringPointId == timeSeries.MeteringPointId)
            .Where(a => a.SettlementPeriod.Start == timeSeries.Period.Start)
            .Where(a => a.SettlementPeriod.End == timeSeries.Period.End)
            .Where(a => a.Status == SettlementStatus.Invoiced)
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
                timeSeries, supply, existingInvoicedSettlement, activePrices);

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
                timeSeries, supply, activePrices);

            db.Settlements.Add(settlement);

            _logger.LogInformation(
                "Created settlement {SettlementId} for metering_point {MpId}: {Amount} DKK, {Energy} kWh",
                settlement.Id,
                timeSeries.MeteringPointId,
                settlement.TotalAmount.Amount,
                settlement.TotalEnergy.Value);
        }
    }
}
