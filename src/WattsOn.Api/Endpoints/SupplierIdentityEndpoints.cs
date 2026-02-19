using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SupplierIdentityEndpoints
{
    public static WebApplication MapSupplierIdentityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/supplier-identities", async (bool? includeArchived, WattsOnDbContext db) =>
        {
            var query = db.SupplierIdentities.AsNoTracking();
            if (includeArchived != true)
                query = query.Where(s => !s.IsArchived);

            var identities = await query
                .OrderBy(s => s.Name)
                .Select(s => new
                {
                    s.Id,
                    Gln = s.Gln.Value,
                    s.Name,
                    Cvr = s.Cvr != null ? s.Cvr.Value : null,
                    s.IsActive,
                    s.IsArchived,
                    s.CreatedAt
                })
                .ToListAsync();
            return Results.Ok(identities);
        }).WithName("GetSupplierIdentities");

        app.MapPost("/api/supplier-identities", async (CreateSupplierIdentityRequest req, WattsOnDbContext db) =>
        {
            try
            {
                var gln = GlnNumber.Create(req.Gln);
                var cvr = req.Cvr != null ? CvrNumber.Create(req.Cvr) : null;

                var identity = SupplierIdentity.Create(gln, req.Name, cvr, req.IsActive);

                db.SupplierIdentities.Add(identity);
                await db.SaveChangesAsync();

                return Results.Created($"/api/supplier-identities/{identity.Id}", new { identity.Id, Gln = identity.Gln.Value, identity.Name });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return Results.Conflict(new { error = "A supplier identity with this GLN already exists." });
            }
        }).WithName("CreateSupplierIdentity");

        app.MapPatch("/api/supplier-identities/{id:guid}", async (Guid id, PatchSupplierIdentityRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FindAsync(id);
            if (identity is null) return Results.NotFound();

            if (req.IsActive.HasValue)
            {
                if (req.IsActive.Value) identity.Activate();
                else identity.Deactivate();
            }

            if (req.Name != null) identity.UpdateName(req.Name);

            if (req.Cvr != null)
            {
                var cvr = string.IsNullOrWhiteSpace(req.Cvr) ? null : CvrNumber.Create(req.Cvr);
                identity.UpdateCvr(cvr);
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { identity.Id, Gln = identity.Gln.Value, identity.Name, Cvr = identity.Cvr?.Value, identity.IsActive, identity.IsArchived });
        }).WithName("PatchSupplierIdentity");

        app.MapPost("/api/supplier-identities/{id:guid}/archive", async (Guid id, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FindAsync(id);
            if (identity is null) return Results.NotFound();
            identity.Archive();
            await db.SaveChangesAsync();
            return Results.Ok(new { identity.Id, Gln = identity.Gln.Value, identity.Name, identity.IsActive, identity.IsArchived });
        }).WithName("ArchiveSupplierIdentity");

        app.MapPost("/api/supplier-identities/{id:guid}/unarchive", async (Guid id, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FindAsync(id);
            if (identity is null) return Results.NotFound();
            identity.Unarchive();
            await db.SaveChangesAsync();
            return Results.Ok(new { identity.Id, Gln = identity.Gln.Value, identity.Name, identity.IsActive, identity.IsArchived });
        }).WithName("UnarchiveSupplierIdentity");

        return app;
    }
}
