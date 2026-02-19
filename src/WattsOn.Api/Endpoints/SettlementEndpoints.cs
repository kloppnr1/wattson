using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SettlementEndpoints
{
    public static WebApplication MapSettlementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/settlements", async (WattsOnDbContext db) =>
        {
            var settlements = await db.Settlements
                .AsNoTracking()
                .OrderByDescending(a => a.CalculatedAt)
                .Take(100)
                .Select(a => new
                {
                    a.Id,
                    a.MeteringPointId,
                    a.SupplyId,
                    PeriodStart = a.SettlementPeriod.Start,
                    PeriodEnd = a.SettlementPeriod.End,
                    TotalEnergyKwh = a.TotalEnergy.Value,
                    TotalAmount = a.TotalAmount.Amount,
                    Currency = a.TotalAmount.Currency,
                    Status = a.Status.ToString(),
                    a.IsCorrection,
                    a.PreviousSettlementId,
                    a.ExternalInvoiceReference,
                    a.InvoicedAt,
                    a.CalculatedAt
                })
                .ToListAsync();
            return Results.Ok(settlements);
        }).WithName("GetSettlements");

        /// <summary>
        /// Uninvoiced settlements — for external invoicing system to pick up.
        /// Returns settlements with status = Beregnet that are NOT corrections.
        /// </summary>
        app.MapGet("/api/settlements/uninvoiced", async (WattsOnDbContext db) =>
        {
            var uninvoiced = await db.Settlements
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                .Where(a => a.Status == SettlementStatus.Calculated && !a.IsCorrection)
                .AsNoTracking()
                .OrderBy(a => a.CalculatedAt)
                .Select(a => new
                {
                    a.Id,
                    Gsrn = a.MeteringPoint.Gsrn.Value,
                    CustomerId = a.Supply.CustomerId,
                    CustomerNavn = a.Supply.Customer.Name,
                    PeriodStart = a.SettlementPeriod.Start,
                    PeriodEnd = a.SettlementPeriod.End,
                    TotalEnergyKwh = a.TotalEnergy.Value,
                    TotalAmount = a.TotalAmount.Amount,
                    Currency = a.TotalAmount.Currency,
                    a.CalculatedAt
                })
                .ToListAsync();
            return Results.Ok(uninvoiced);
        }).WithName("GetUninvoicedSettlements");

        /// <summary>
        /// Adjustment settlements — corrections of already-invoiced settlements.
        /// External invoicing system picks these up to issue credit/debit notes.
        /// </summary>
        app.MapGet("/api/settlements/adjustments", async (WattsOnDbContext db) =>
        {
            var adjustments = await db.Settlements
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                .Where(a => a.Status == SettlementStatus.Calculated && a.IsCorrection)
                .AsNoTracking()
                .OrderBy(a => a.CalculatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.PreviousSettlementId,
                    Gsrn = a.MeteringPoint.Gsrn.Value,
                    CustomerId = a.Supply.CustomerId,
                    CustomerNavn = a.Supply.Customer.Name,
                    PeriodStart = a.SettlementPeriod.Start,
                    PeriodEnd = a.SettlementPeriod.End,
                    DeltaEnergyKwh = a.TotalEnergy.Value,
                    DeltaAmount = a.TotalAmount.Amount,
                    Currency = a.TotalAmount.Currency,
                    a.CalculatedAt
                })
                .ToListAsync();
            return Results.Ok(adjustments);
        }).WithName("GetAdjustmentSettlements");

        /// <summary>
        /// Get the latest settlement for a metering point — used to watch the settlement engine work.
        /// Returns the full calculation breakdown (lines, prices, quantities, amounts).
        /// </summary>
        app.MapGet("/api/settlements/by-metering-point/{meteringPointId:guid}", async (Guid meteringPointId, WattsOnDbContext db) =>
        {
            var settlement = await db.Settlements
                .Include(a => a.Lines)
                .Include(a => a.MeteringPoint)
                .AsNoTracking()
                .Where(a => a.MeteringPointId == meteringPointId)
                .OrderByDescending(a => a.CalculatedAt)
                .FirstOrDefaultAsync();

            if (settlement is null) return Results.Ok(new { found = false });

            return Results.Ok(new
            {
                found = true,
                id = settlement.Id,
                meteringPointId = settlement.MeteringPointId,
                gsrn = settlement.MeteringPoint.Gsrn.Value,
                periodStart = settlement.SettlementPeriod.Start,
                periodEnd = settlement.SettlementPeriod.End,
                totalEnergyKwh = settlement.TotalEnergy.Value,
                totalAmount = settlement.TotalAmount.Amount,
                currency = settlement.TotalAmount.Currency,
                status = settlement.Status.ToString(),
                isCorrection = settlement.IsCorrection,
                timeSeriesVersion = settlement.TimeSeriesVersion,
                calculatedAt = settlement.CalculatedAt,
                lines = settlement.Lines.Select(l => new
                {
                    l.Id,
                    l.PriceId,
                    l.Description,
                    quantityKwh = l.Quantity.Value,
                    unitPrice = l.UnitPrice,
                    amount = l.Amount.Amount,
                    currency = l.Amount.Currency,
                }).ToList()
            });
        }).WithName("GetSettlementByMeteringPoint");

        /// <summary>
        /// External invoicing system confirms a settlement has been invoiced.
        /// </summary>
        app.MapPost("/api/settlements/{id:guid}/mark-invoiced", async (Guid id, MarkInvoicedRequest req, WattsOnDbContext db) =>
        {
            var settlement = await db.Settlements.FindAsync(id);
            if (settlement is null) return Results.NotFound();

            try
            {
                settlement.MarkInvoiced(req.ExternalInvoiceReference);
                await db.SaveChangesAsync();
                return Results.Ok(new
                {
                    settlement.Id,
                    Status = settlement.Status.ToString(),
                    settlement.ExternalInvoiceReference,
                    settlement.InvoicedAt
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        }).WithName("MarkSettlementInvoiced");

        // ==================== SETTLEMENT ISSUES ====================

        app.MapGet("/api/settlement-issues", async (string? status, Guid? meteringPointId, WattsOnDbContext db) =>
        {
            var query = db.SettlementIssues.AsNoTracking().AsQueryable();

            if (!string.IsNullOrEmpty(status) && Enum.TryParse<SettlementIssueStatus>(status, true, out var parsedStatus))
                query = query.Where(i => i.Status == parsedStatus);
            else
                query = query.Where(i => i.Status == SettlementIssueStatus.Open); // Default: open only

            if (meteringPointId.HasValue)
                query = query.Where(i => i.MeteringPointId == meteringPointId.Value);

            var issues = await query
                .OrderByDescending(i => i.CreatedAt)
                .Take(200)
                .Select(i => new
                {
                    i.Id,
                    i.MeteringPointId,
                    i.TimeSeriesId,
                    i.TimeSeriesVersion,
                    PeriodStart = i.Period.Start,
                    PeriodEnd = i.Period.End,
                    IssueType = i.IssueType.ToString(),
                    i.Message,
                    i.Details,
                    Status = i.Status.ToString(),
                    i.ResolvedAt,
                    i.CreatedAt,
                })
                .ToListAsync();

            return Results.Ok(issues);
        }).WithName("GetSettlementIssues");

        app.MapGet("/api/settlement-issues/count", async (WattsOnDbContext db) =>
        {
            var openCount = await db.SettlementIssues.CountAsync(i => i.Status == SettlementIssueStatus.Open);
            return Results.Ok(new { open = openCount });
        }).WithName("GetSettlementIssueCount");

        app.MapPost("/api/settlement-issues/{id:guid}/dismiss", async (Guid id, WattsOnDbContext db) =>
        {
            var issue = await db.SettlementIssues.FindAsync(id);
            if (issue is null) return Results.NotFound();

            issue.Dismiss();
            await db.SaveChangesAsync();

            return Results.Ok(new { issue.Id, Status = issue.Status.ToString(), issue.ResolvedAt });
        }).WithName("DismissSettlementIssue");

        return app;
    }
}
