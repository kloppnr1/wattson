using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static WebApplication MapReconciliationEndpoints(this WebApplication app)
    {
        // ==================== LIST RECONCILIATION RESULTS ====================

        app.MapGet("/api/reconciliation", async (WattsOnDbContext db) =>
        {
            var results = await db.ReconciliationResults
                .AsNoTracking()
                .OrderByDescending(r => r.ReconciliationDate)
                .Take(100)
                .Select(r => new
                {
                    r.Id,
                    r.GridArea,
                    PeriodStart = r.Period.Start,
                    PeriodEnd = r.Period.End,
                    r.OurTotalDkk,
                    r.DataHubTotalDkk,
                    r.DifferenceDkk,
                    r.DifferencePercent,
                    Status = r.Status.ToString(),
                    r.ReconciliationDate,
                    r.WholesaleSettlementId,
                    r.Notes
                })
                .ToListAsync();

            return Results.Ok(results);
        }).WithName("GetReconciliationResults");

        // ==================== GET RECONCILIATION DETAIL ====================

        app.MapGet("/api/reconciliation/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var result = await db.ReconciliationResults
                .Include(r => r.Lines)
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == id);

            if (result is null) return Results.NotFound();

            return Results.Ok(new
            {
                result.Id,
                result.GridArea,
                PeriodStart = result.Period.Start,
                PeriodEnd = result.Period.End,
                result.OurTotalDkk,
                result.DataHubTotalDkk,
                result.DifferenceDkk,
                result.DifferencePercent,
                Status = result.Status.ToString(),
                result.ReconciliationDate,
                result.WholesaleSettlementId,
                result.Notes,
                Lines = result.Lines.Select(l => new
                {
                    l.ChargeId,
                    l.ChargeType,
                    l.OurAmount,
                    l.DataHubAmount,
                    l.Difference
                })
            });
        }).WithName("GetReconciliationDetail");

        // ==================== RUN RECONCILIATION ====================

        app.MapPost("/api/reconciliation/run", async (RunReconciliationRequest req, WattsOnDbContext db) =>
        {
            var period = Period.Create(req.StartDate, req.EndDate);

            // Find our settlements for this grid area + period
            // We match settlements whose metering points belong to the requested grid area
            var ourSettlements = await db.Settlements
                .Include(s => s.Lines)
                .Include(s => s.MeteringPoint)
                .Where(s => s.MeteringPoint.GridArea == req.GridArea)
                .Where(s => s.SettlementPeriod.Start >= req.StartDate && s.SettlementPeriod.Start < req.EndDate)
                .AsNoTracking()
                .ToListAsync();

            // Aggregate our lines by charge description (best approximation for ChargeId)
            var ourLines = ourSettlements
                .SelectMany(s => s.Lines)
                .GroupBy(l => new { l.Description })
                .Select(g => new ReconciliationLineInput(
                    g.Key.Description,
                    "D03", // Tariff as default — our settlements don't store charge type
                    g.Sum(l => l.Amount.Amount)))
                .ToList();

            // Find the DataHub wholesale settlement for this grid area + period
            var wholesaleSettlement = await db.WholesaleSettlements
                .Include(ws => ws.Lines)
                .Where(ws => ws.GridArea == req.GridArea)
                .Where(ws => ws.Period.Start >= req.StartDate && ws.Period.Start < req.EndDate)
                .OrderByDescending(ws => ws.ReceivedAt)
                .AsNoTracking()
                .FirstOrDefaultAsync();

            var result = ReconciliationResult.Reconcile(
                req.GridArea, period, ourLines, wholesaleSettlement);

            db.ReconciliationResults.Add(result);
            await db.SaveChangesAsync();

            return Results.Created($"/api/reconciliation/{result.Id}", new
            {
                result.Id,
                result.GridArea,
                PeriodStart = result.Period.Start,
                PeriodEnd = result.Period.End,
                result.OurTotalDkk,
                result.DataHubTotalDkk,
                result.DifferenceDkk,
                result.DifferencePercent,
                Status = result.Status.ToString(),
                result.ReconciliationDate,
                result.WholesaleSettlementId,
                LineCount = result.Lines.Count
            });
        }).WithName("RunReconciliation");

        return app;
    }
}
