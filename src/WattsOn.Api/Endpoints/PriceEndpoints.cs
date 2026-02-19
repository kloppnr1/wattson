using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class PriceEndpoints
{
    public static WebApplication MapPriceEndpoints(this WebApplication app)
    {
        app.MapGet("/api/prices", async (WattsOnDbContext db) =>
        {
            var prices = await db.Prices
                .AsNoTracking()
                .OrderBy(p => p.ChargeId)
                .Select(p => new
                {
                    p.Id,
                    p.ChargeId,
                    OwnerGln = p.OwnerGln.Value,
                    Type = p.Type.ToString(),
                    p.Description,
                    ValidFrom = p.ValidityPeriod.Start,
                    ValidTo = p.ValidityPeriod.End,
                    p.VatExempt,
                    p.IsTax,
                    p.IsPassThrough,
                    PriceResolution = p.PriceResolution != null ? p.PriceResolution.ToString() : null,
                    PricePointCount = p.PricePoints.Count,
                    LinkedMeteringPoints = db.PriceLinks.Count(pl => pl.PriceId == p.Id)
                })
                .ToListAsync();
            return Results.Ok(prices);
        }).WithName("GetPrices");

        app.MapGet("/api/prices/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var price = await db.Prices
                .AsNoTracking()
                .Include(p => p.PricePoints)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (price is null) return Results.NotFound();

            var links = await db.PriceLinks
                .AsNoTracking()
                .Include(pl => pl.MeteringPoint)
                .Where(pl => pl.PriceId == id)
                .Select(pl => new
                {
                    pl.Id,
                    pl.MeteringPointId,
                    Gsrn = pl.MeteringPoint.Gsrn.Value,
                    LinkFrom = pl.LinkPeriod.Start,
                    LinkTo = pl.LinkPeriod.End
                })
                .ToListAsync();

            return Results.Ok(new
            {
                price.Id,
                price.ChargeId,
                OwnerGln = price.OwnerGln.Value,
                Type = price.Type.ToString(),
                price.Description,
                ValidFrom = price.ValidityPeriod.Start,
                ValidTo = price.ValidityPeriod.End,
                price.VatExempt,
                price.IsTax,
                price.IsPassThrough,
                PriceResolution = price.PriceResolution?.ToString(),
                PricePoints = price.PricePoints
                    .OrderByDescending(pp => pp.Timestamp)
                    .Take(100)
                    .Select(pp => new { pp.Timestamp, pp.Price }),
                TotalPricePoints = price.PricePoints.Count,
                LinkedMeteringPoints = links
            });
        }).WithName("GetPrice");

        app.MapPost("/api/prices", async (CreatePrisRequest req, WattsOnDbContext db) =>
        {
            var ownerGln = GlnNumber.Create(req.OwnerGln);
            var type = Enum.Parse<PriceType>(req.Type);
            var validityPeriod = req.ValidTo.HasValue
                ? Period.Create(req.ValidFrom, req.ValidTo.Value)
                : Period.From(req.ValidFrom);
            var resolution = req.PriceResolution != null ? Enum.Parse<Resolution>(req.PriceResolution) : (Resolution?)null;

            var pris = Price.Create(req.ChargeId, ownerGln, type, req.Description, validityPeriod, req.VatExempt, resolution);

            if (req.PricePoints != null)
            {
                foreach (var pp in req.PricePoints)
                {
                    pris.AddPricePoint(pp.Timestamp, pp.Price);
                }
            }

            db.Prices.Add(pris);
            await db.SaveChangesAsync();

            return Results.Created($"/api/prices/{pris.Id}", new
            {
                pris.Id,
                pris.ChargeId,
                Type = pris.Type.ToString(),
                pris.Description,
                PricePointCount = pris.PricePoints.Count
            });
        }).WithName("CreatePrice");

        // ==================== PRISTILKNYTNINGER (Price Links) ====================

        app.MapGet("/api/price-links", async (Guid? meteringPointId, WattsOnDbContext db) =>
        {
            var query = db.PriceLinks
                .Include(pt => pt.Price)
                .AsNoTracking();

            if (meteringPointId.HasValue)
                query = query.Where(pt => pt.MeteringPointId == meteringPointId.Value);

            var links = await query
                .Select(pt => new
                {
                    pt.Id,
                    pt.MeteringPointId,
                    pt.PriceId,
                    ChargeId = pt.Price.ChargeId,
                    PrisDescription = pt.Price.Description,
                    PrisType = pt.Price.Type.ToString(),
                    LinkFrom = pt.LinkPeriod.Start,
                    LinkTo = pt.LinkPeriod.End
                })
                .ToListAsync();
            return Results.Ok(links);
        }).WithName("GetPriceLinks");

        app.MapPost("/api/price-links", async (CreatePriceLinkRequest req, WattsOnDbContext db) =>
        {
            var mp = await db.MeteringPoints.FindAsync(req.MeteringPointId);
            if (mp is null) return Results.BadRequest(new { error = "MeteringPoint not found" });

            var pris = await db.Prices.FindAsync(req.PriceId);
            if (pris is null) return Results.BadRequest(new { error = "Price not found" });

            var linkPeriod = req.LinkTo.HasValue
                ? Period.Create(req.LinkFrom, req.LinkTo.Value)
                : Period.From(req.LinkFrom);

            var link = PriceLink.Create(req.MeteringPointId, req.PriceId, linkPeriod);

            db.PriceLinks.Add(link);
            await db.SaveChangesAsync();

            return Results.Created($"/api/price_links/{link.Id}", new { link.Id });
        }).WithName("CreatePriceLink");

        return app;
    }
}
