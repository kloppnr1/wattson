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
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
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
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
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
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
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

        // ==================== BRS-003: INCORRECT SUPPLIER SWITCH ====================

        app.MapPost("/api/processes/incorrect-switch", async (IncorrectSwitchRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs003Handler.InitiateReversal(gsrn, req.SwitchDate, supplierGln, req.Reason ?? "Fejlagtigt leverandørskift");

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
                SwitchDate = req.SwitchDate
            });
        }).WithName("InitiateIncorrectSwitch");

        // ==================== BRS-011: INCORRECT MOVE ====================

        app.MapPost("/api/processes/incorrect-move", async (IncorrectMoveRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs011Handler.InitiateReversal(gsrn, req.MoveDate, supplierGln, req.MoveType, req.Reason ?? "Fejlagtig flytning");

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
                MoveDate = req.MoveDate,
                MoveType = req.MoveType
            });
        }).WithName("InitiateIncorrectMove");

        // ==================== BRS-034: REQUEST PRICES ====================

        app.MapPost("/api/processes/request-prices", async (RequestPricesRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);

            var result = req.RequestType == "D48"
                ? Brs034Handler.RequestPriceSeries(supplierGln, req.StartDate, req.EndDate, req.PriceOwnerGln, req.PriceType, req.ChargeId)
                : Brs034Handler.RequestPriceInformation(supplierGln, req.StartDate, req.EndDate, req.PriceOwnerGln, req.PriceType, req.ChargeId);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                RequestType = req.RequestType
            });
        }).WithName("RequestPrices");

        // ==================== BRS-038: REQUEST CHARGE LINKS ====================

        app.MapPost("/api/processes/request-charge-links", async (RequestChargeLinksRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs038Handler.RequestChargeLinks(gsrn, supplierGln, req.StartDate, req.EndDate);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                Gsrn = req.Gsrn
            });
        }).WithName("RequestChargeLinks");

        // ==================== BRS-023: REQUEST AGGREGATED DATA ====================

        app.MapPost("/api/processes/request-aggregated-data", async (RequestAggregatedDataRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs023RequestHandler.RequestAggregatedData(
                supplierGln, req.GridArea, req.StartDate, req.EndDate,
                req.MeteringPointType, req.ProcessType);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                GridArea = req.GridArea,
                MeteringPointType = req.MeteringPointType
            });
        }).WithName("RequestAggregatedData");

        // ==================== BRS-027: REQUEST WHOLESALE SETTLEMENT ====================

        app.MapPost("/api/processes/request-wholesale-settlement", async (RequestWholesaleSettlementRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var chargeTypes = req.ChargeTypes?.Select(ct =>
                new Brs027RequestHandler.ChargeTypeFilter(ct.ChargeId, ct.Type)).ToList();

            var result = Brs027RequestHandler.RequestWholesaleSettlement(
                supplierGln, req.GridArea, req.StartDate, req.EndDate,
                req.EnergySupplierGln, req.ChargeTypeOwnerGln, chargeTypes);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                GridArea = req.GridArea
            });
        }).WithName("RequestWholesaleSettlement");

        // ==================== BRS-005: REQUEST MASTER DATA ====================

        app.MapPost("/api/processes/request-master-data", async (RequestMasterDataRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs005Handler.RequestMasterData(gsrn, supplierGln);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                Gsrn = req.Gsrn
            });
        }).WithName("RequestMasterData");

        // ==================== BRS-024: REQUEST YEARLY SUM ====================

        app.MapPost("/api/processes/request-yearly-sum", async (RequestYearlySumRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs024Handler.RequestYearlySum(gsrn, supplierGln);

            db.Processes.Add(result.Process);
            db.OutboxMessages.Add(result.OutboxMessage);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processes/{result.Process.Id}", new
            {
                result.Process.Id,
                ProcessType = result.Process.ProcessType.ToString(),
                Status = result.Process.Status.ToString(),
                result.Process.CurrentState,
                Gsrn = req.Gsrn
            });
        }).WithName("RequestYearlySum");

        // ==================== BRS-025: REQUEST METERED DATA ====================

        app.MapPost("/api/processes/request-metered-data", async (RequestMeteredDataRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs025Handler.RequestMeteredData(gsrn, supplierGln, req.StartDate, req.EndDate);

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
                StartDate = req.StartDate,
                EndDate = req.EndDate
            });
        }).WithName("RequestMeteredData");

        // ==================== BRS-039: SERVICE REQUEST ====================

        app.MapPost("/api/processes/service-request", async (ServiceRequestDto req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs039Handler.RequestService(gsrn, supplierGln, req.ServiceType, req.RequestedDate, req.Reason);

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
                ServiceType = req.ServiceType
            });
        }).WithName("ServiceRequest");

        // ==================== BRS-041: ELECTRICAL HEATING ====================

        app.MapPost("/api/processes/electrical-heating", async (ElectricalHeatingRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value);
            if (mp is null) return Results.BadRequest(new { error = "Metering point not found" });

            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive);
            if (identity is null) return Results.BadRequest(new { error = "No active supplier identity" });

            var supplierGln = GlnNumber.Create(identity.Gln.Value);
            var result = Brs041Handler.RequestElectricalHeatingChange(gsrn, supplierGln, req.Action, req.EffectiveDate);

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
                Action = req.Action
            });
        }).WithName("ElectricalHeating");

        return app;
    }
}
