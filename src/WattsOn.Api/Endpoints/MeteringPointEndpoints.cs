using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class MeteringPointEndpoints
{
    public static WebApplication MapMeteringPointEndpoints(this WebApplication app)
    {
        app.MapGet("/api/metering-points", async (WattsOnDbContext db) =>
        {
            var mp = await db.MeteringPoints
                .AsNoTracking()
                .OrderBy(m => m.Gsrn.Value)
                .Select(m => new
                {
                    m.Id,
                    Gsrn = m.Gsrn.Value,
                    Type = m.Type.ToString(),
                    Art = m.Art.ToString(),
                    SettlementMethod = m.SettlementMethod.ToString(),
                    Resolution = m.Resolution.ToString(),
                    ConnectionState = m.ConnectionState.ToString(),
                    m.GridArea,
                    GridCompanyGln = m.GridCompanyGln.Value,
                    m.HasActiveSupply,
                    m.CreatedAt
                })
                .ToListAsync();
            return Results.Ok(mp);
        }).WithName("GetMeteringPoints");

        app.MapGet("/api/metering-points/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var mp = await db.MeteringPoints
                .Include(m => m.Supplies)
                    .ThenInclude(l => l.Customer)
                .Include(m => m.TimeSeriesCollection)
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == id);

            if (mp is null) return Results.NotFound();

            return Results.Ok(new
            {
                mp.Id,
                Gsrn = mp.Gsrn.Value,
                Type = mp.Type.ToString(),
                Art = mp.Art.ToString(),
                SettlementMethod = mp.SettlementMethod.ToString(),
                Resolution = mp.Resolution.ToString(),
                ConnectionState = mp.ConnectionState.ToString(),
                mp.GridArea,
                GridCompanyGln = mp.GridCompanyGln.Value,
                mp.HasActiveSupply,
                Address = mp.Address != null ? new
                {
                    mp.Address.StreetName,
                    mp.Address.BuildingNumber,
                    mp.Address.Floor,
                    mp.Address.Suite,
                    mp.Address.PostCode,
                    mp.Address.CityName
                } : null,
                mp.CreatedAt,
                Supplies = mp.Supplies.Select(l => new
                {
                    l.Id,
                    l.CustomerId,
                    CustomerName = l.Customer.Name,
                    SupplyStart = l.SupplyPeriod.Start,
                    SupplyEnd = l.SupplyPeriod.End,
                    l.IsActive
                }),
                Time_series = mp.TimeSeriesCollection.OrderByDescending(t => t.ReceivedAt).Select(t => new
                {
                    t.Id,
                    PeriodStart = t.Period.Start,
                    PeriodEnd = t.Period.End,
                    Resolution = t.Resolution.ToString(),
                    t.Version,
                    t.IsLatest,
                    t.ReceivedAt
                })
            });
        }).WithName("GetMeteringPoint");

        app.MapPost("/api/metering-points", async (CreateMeteringPointRequest req, WattsOnDbContext db) =>
        {
            var gsrn = Gsrn.Create(req.Gsrn);
            var gridCompanyGln = GlnNumber.Create(req.GridCompanyGln);
            var type = Enum.Parse<MeteringPointType>(req.Type);
            var art = Enum.Parse<MeteringPointCategory>(req.Art);
            var settlement = Enum.Parse<SettlementMethod>(req.SettlementMethod);
            var resolution = Enum.Parse<Resolution>(req.Resolution);

            Address? address = req.Address != null
                ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber, req.Address.PostCode, req.Address.CityName,
                    req.Address.Floor, req.Address.Suite)
                : null;

            var mp = MeteringPoint.Create(gsrn, type, art, settlement, resolution, req.GridArea, gridCompanyGln, address);

            db.MeteringPoints.Add(mp);
            await db.SaveChangesAsync();

            return Results.Created($"/api/metering-points/{mp.Id}", new { mp.Id, Gsrn = mp.Gsrn.Value });
        }).WithName("CreateMeteringPoint");

        return app;
    }
}
