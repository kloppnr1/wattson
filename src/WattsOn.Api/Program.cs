using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Endpoints;
using WattsOn.Domain.Entities;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure;
using WattsOn.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                  ?? ["http://localhost:5173"];
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Auto-migrate + seed on startup (dev + staging)
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
    await db.Database.MigrateAsync();

    // Seed supplier identities
    if (!await db.SupplierIdentities.AnyAsync())
    {
        db.SupplierIdentities.AddRange(
            SupplierIdentity.Create(GlnNumber.Create("5790002529283"), "Hjerting Handel"),
            SupplierIdentity.Create(GlnNumber.Create("5790002388309"), "Aars Nibe Handel"),
            SupplierIdentity.Create(GlnNumber.Create("5790001103040"), "Midtjysk Elhandel"),
            SupplierIdentity.Create(GlnNumber.Create("5790001103033"), "Verdo Go Green")
        );
        await db.SaveChangesAsync();
    }
    app.MapOpenApi();
}

app.UseCors();

// Global validation error handler — catches domain ArgumentExceptions (GLN, CVR, GSRN etc.)
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (ArgumentException ex) when (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = ex.Message });
    }
});

// Map all endpoint groups
app.MapHealthEndpoints();
app.MapSupplierIdentityEndpoints();
app.MapCustomerEndpoints();
app.MapMeteringPointEndpoints();
app.MapSupplyEndpoints();
app.MapProcessEndpoints();
app.MapInboxOutboxEndpoints();
app.MapSettlementEndpoints();
app.MapPriceEndpoints();
app.MapSpotPriceEndpoints();
app.MapTimeSeriesEndpoints();
app.MapSettlementDocumentEndpoints();
app.MapSimulationEndpoints();
app.MapDashboardEndpoints();
app.MapReconciliationEndpoints();
app.MapAdminEndpoints();
app.MapMigrationEndpoints();

app.Run();
