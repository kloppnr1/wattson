using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class CustomerEndpoints
{
    public static WebApplication MapCustomerEndpoints(this WebApplication app)
    {
        app.MapGet("/api/customers", async (WattsOnDbContext db) =>
        {
            var customers = await db.Customers
                .Include(k => k.SupplierIdentity)
                .AsNoTracking()
                .OrderBy(k => k.Name)
                .Select(k => new
                {
                    k.Id,
                    k.Name,
                    Cpr = k.Cpr != null ? k.Cpr.Value : null,
                    Cvr = k.Cvr != null ? k.Cvr.Value : null,
                    k.Email,
                    k.Phone,
                    IsPrivate = k.Cpr != null,
                    IsCompany = k.Cvr != null,
                    k.SupplierIdentityId,
                    SupplierGln = k.SupplierIdentity.Gln.Value,
                    SupplierName = k.SupplierIdentity.Name,
                    k.CreatedAt
                })
                .ToListAsync();
            return Results.Ok(customers);
        }).WithName("GetCustomers");

        app.MapGet("/api/customers/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var customer = await db.Customers
                .Include(k => k.SupplierIdentity)
                .Include(k => k.Supplies)
                    .ThenInclude(l => l.MeteringPoint)
                .AsNoTracking()
                .FirstOrDefaultAsync(k => k.Id == id);

            if (customer is null) return Results.NotFound();

            return Results.Ok(new
            {
                customer.Id,
                customer.Name,
                Cpr = customer.Cpr?.Value,
                Cvr = customer.Cvr?.Value,
                customer.Email,
                customer.Phone,
                customer.SupplierIdentityId,
                SupplierGln = customer.SupplierIdentity.Gln.Value,
                SupplierName = customer.SupplierIdentity.Name,
                Address = customer.Address != null ? new
                {
                    customer.Address.StreetName,
                    customer.Address.BuildingNumber,
                    customer.Address.Floor,
                    customer.Address.Suite,
                    customer.Address.PostCode,
                    customer.Address.CityName
                } : null,
                IsPrivate = customer.Cpr != null,
                IsCompany = customer.Cvr != null,
                customer.CreatedAt,
                Supplies = customer.Supplies.Select(l => new
                {
                    l.Id,
                    l.MeteringPointId,
                    Gsrn = l.MeteringPoint.Gsrn.Value,
                    SupplyStart = l.SupplyPeriod.Start,
                    SupplyEnd = l.SupplyPeriod.End,
                    l.IsActive
                })
            });
        }).WithName("GetCustomer");

        app.MapPost("/api/customers", async (CreateCustomerRequest req, WattsOnDbContext db) =>
        {
            Address? address = req.Address != null
                ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber, req.Address.PostCode, req.Address.CityName,
                    req.Address.Floor, req.Address.Suite)
                : null;

            // Verify supplier identity exists
            var supplierIdentity = await db.SupplierIdentities.FindAsync(req.SupplierIdentityId);
            if (supplierIdentity is null) return Results.BadRequest(new { error = "Supplier identity not found" });

            Customer customer;
            if (req.Cpr != null)
            {
                customer = Customer.CreatePerson(req.Name, CprNumber.Create(req.Cpr), req.SupplierIdentityId, address);
            }
            else if (req.Cvr != null)
            {
                customer = Customer.CreateCompany(req.Name, CvrNumber.Create(req.Cvr), req.SupplierIdentityId, address);
            }
            else
            {
                return Results.BadRequest(new { error = "Either CPR or CVR is required" });
            }

            if (req.Email != null || req.Phone != null)
                customer.UpdateContactInfo(req.Email, req.Phone);

            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            return Results.Created($"/api/customers/{customer.Id}", new { customer.Id, customer.Name });
        }).WithName("CreateCustomer");

        return app;
    }
}
