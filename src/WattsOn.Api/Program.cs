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
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Auto-migrate + seed own actor on startup (dev only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
    await db.Database.MigrateAsync();

    // Ensure at least one supplier identity exists
    if (!await db.SupplierIdentities.AnyAsync())
    {
        var identity = SupplierIdentity.Create(
            GlnNumber.Create("5790001330552"),
            "WattsOn Energy A/S",
            CvrNumber.Create("12345678"));
        db.SupplierIdentities.Add(identity);
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
app.MapTimeSeriesEndpoints();
app.MapSettlementDocumentEndpoints();
app.MapSimulationEndpoints();
app.MapDashboardEndpoints();
app.MapReconciliationEndpoints();

app.Run();
