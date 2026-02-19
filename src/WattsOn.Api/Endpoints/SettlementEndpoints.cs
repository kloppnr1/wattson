using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
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

        return app;
    }
}
