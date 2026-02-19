using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SupplyEndpoints
{
    public static WebApplication MapSupplyEndpoints(this WebApplication app)
    {
        app.MapGet("/api/supplies", async (WattsOnDbContext db) =>
        {
            var supplies = await db.Supplies
                .Include(l => l.Customer)
                .Include(l => l.MeteringPoint)
                .AsNoTracking()
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new
                {
                    l.Id,
                    l.MeteringPointId,
                    Gsrn = l.MeteringPoint.Gsrn.Value,
                    l.CustomerId,
                    CustomerName = l.Customer.Name,
                    SupplyStart = l.SupplyPeriod.Start,
                    SupplyEnd = l.SupplyPeriod.End,
                    l.IsActive,
                    l.CreatedAt
                })
                .ToListAsync();
            return Results.Ok(supplies);
        }).WithName("GetSupplies");

        app.MapPost("/api/supplies", async (CreateSupplyRequest req, WattsOnDbContext db) =>
        {
            // Verify references exist
            var mp = await db.MeteringPoints.FindAsync(req.MeteringPointId);
            if (mp is null) return Results.BadRequest(new { error = "MeteringPoint not found" });

            var customer = await db.Customers.FindAsync(req.CustomerId);
            if (customer is null) return Results.BadRequest(new { error = "Customer not found" });

            var supplyPeriod = req.SupplyEnd.HasValue
                ? Period.Create(req.SupplyStart, req.SupplyEnd.Value)
                : Period.From(req.SupplyStart);

            var supply = Supply.Create(req.MeteringPointId, req.CustomerId, supplyPeriod);

            mp.SetActiveSupply(true);

            db.Supplies.Add(supply);
            await db.SaveChangesAsync();

            return Results.Created($"/api/supplies/{supply.Id}", new { supply.Id });
        }).WithName("CreateSupply");

        return app;
    }
}
