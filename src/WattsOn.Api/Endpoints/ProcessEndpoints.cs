using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class ProcessEndpoints
{
    public static WebApplication MapProcessEndpoints(this WebApplication app)
    {
        app.MapGet("/api/processes", async (WattsOnDbContext db) =>
        {
            var processer = await db.Processes
                .AsNoTracking()
                .OrderByDescending(p => p.StartedAt)
                .Take(100)
                .Select(p => new
                {
                    p.Id,
                    p.TransactionId,
                    ProcessType = p.ProcessType.ToString(),
                    Role = p.Role.ToString(),
                    Status = p.Status.ToString(),
                    p.CurrentState,
                    MeteringPointGsrn = p.MeteringPointGsrn != null ? p.MeteringPointGsrn.Value : null,
                    p.EffectiveDate,
                    p.StartedAt,
                    p.CompletedAt,
                    p.ErrorMessage
                })
                .ToListAsync();
            return Results.Ok(processer);
        }).WithName("GetProcesser");

        app.MapGet("/api/processer/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var process = await db.Processes
                .Include(p => p.Transitions)
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id);

            if (process is null) return Results.NotFound();

            return Results.Ok(new
            {
                process.Id,
                process.TransactionId,
                ProcessType = process.ProcessType.ToString(),
                Role = process.Role.ToString(),
                Status = process.Status.ToString(),
                process.CurrentState,
                MeteringPointGsrn = process.MeteringPointGsrn?.Value,
                process.EffectiveDate,
                CounterpartGln = process.CounterpartGln?.Value,
                process.StartedAt,
                process.CompletedAt,
                process.ErrorMessage,
                Transitions = process.Transitions.OrderBy(t => t.TransitionedAt).Select(t => new
                {
                    t.FromState,
                    t.ToState,
                    t.Reason,
                    t.TransitionedAt
                })
            });
        }).WithName("GetProcess");

        // ==================== BRS-002: END OF SUPPLY ====================

        app.MapPost("/api/processes/end-of-supply", async (EndOfSupplyRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == gsrn);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var supply = await db.Supplies
                .Where(s => s.MeteringPointId == mp.Id)
                .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > DateTimeOffset.UtcNow)
                .FirstOrDefaultAsync();
            if (supply is null) return Results.BadRequest(new { error = "No active supply for this metering point" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs002Handler.InitiateEndOfSupply(gsrn, req.DesiredEndDate, supplierGln, req.Reason ?? "Contract ended");

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                Gsrn = req.Gsrn,
                DesiredEndDate = req.DesiredEndDate
            });
        }).WithName("InitiateEndOfSupply");

        // ==================== BRS-010: MOVE-OUT ====================

        app.MapPost("/api/processes/move-out", async (MoveOutRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == gsrn);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var supply = await db.Supplies
                .Where(s => s.MeteringPointId == mp.Id)
                .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > DateTimeOffset.UtcNow)
                .FirstOrDefaultAsync();
            if (supply is null) return Results.BadRequest(new { error = "No active supply for this metering point" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs010Handler.ExecuteMoveOut(gsrn, req.EffectiveDate, supply, supplierGln);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                Gsrn = req.Gsrn,
                EffectiveDate = req.EffectiveDate
            });
        }).WithName("InitiateMoveOut");

        // ==================== BRS-015: CUSTOMER DATA UPDATE ====================

        app.MapPost("/api/processes/customer-update", async (CustomerUpdateRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == gsrn);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var supply = await db.Supplies
                .Where(s => s.MeteringPointId == mp.Id)
                .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > DateTimeOffset.UtcNow)
                .FirstOrDefaultAsync();
            if (supply is null) return Results.BadRequest(new { error = "No active supply — cannot update customer data" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);

            Address? address = null;
            if (req.Address is not null)
            {
                address = Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                    req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite);
            }

            var data = new Brs015Handler.CustomerUpdateData(
                req.CustomerName, req.Cpr, req.Cvr, req.Email, req.Phone, address);

            var result = Brs015Handler.SendCustomerUpdate(gsrn, req.EffectiveDate, supplierGln, data);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState
            });
        }).WithName("SendCustomerUpdate");

        return app;
    }
}
