using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.Services;
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

// --- API Endpoints ---

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }))
    .WithName("Health");

// ==================== SUPPLIER IDENTITIES ====================

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

// ==================== KUNDER ====================

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

// ==================== MÅLEPUNKTER ====================

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
            CustomerNavn = l.Customer.Name,
            SupplyStart = l.SupplyPeriod.Start,
            SupplyEnd = l.SupplyPeriod.End,
            l.IsActive
        }),
        TimeSeriesCollection = mp.TimeSeriesCollection.OrderByDescending(t => t.ReceivedAt).Select(t => new
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

// ==================== LEVERANCER ====================

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
            CustomerNavn = l.Customer.Name,
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

// ==================== PROCESSER ====================

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

// ==================== INBOX / OUTBOX ====================

app.MapGet("/api/inbox", async (WattsOnDbContext db, bool? unprocessed) =>
{
    var query = db.InboxMessages.AsNoTracking();
    if (unprocessed == true)
        query = query.Where(m => !m.IsProcessed);

    var messages = await query
        .OrderByDescending(m => m.ReceivedAt)
        .Take(100)
        .Select(m => new
        {
            m.Id,
            m.MessageId,
            m.DocumentType,
            m.BusinessProcess,
            m.SenderGln,
            m.ReceiverGln,
            m.ReceivedAt,
            m.IsProcessed,
            m.ProcessedAt,
            m.ProcessingError,
            m.ProcessingAttempts
        })
        .ToListAsync();
    return Results.Ok(messages);
}).WithName("GetInbox");

app.MapGet("/api/outbox", async (WattsOnDbContext db, bool? unsent) =>
{
    var query = db.OutboxMessages.AsNoTracking();
    if (unsent == true)
        query = query.Where(m => !m.IsSent);

    var messages = await query
        .OrderByDescending(m => m.CreatedAt)
        .Take(100)
        .Select(m => new
        {
            m.Id,
            m.DocumentType,
            m.BusinessProcess,
            m.SenderGln,
            m.ReceiverGln,
            m.IsSent,
            m.SentAt,
            m.SendError,
            m.SendAttempts,
            m.ScheduledFor,
            m.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(messages);
}).WithName("GetOutbox");

// ==================== AFREGNINGER (Settlement) ====================

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

// ==================== PRISER (Prices) ====================

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

// ==================== TIDSSERIER (Time Series) ====================

/// <summary>
/// Ingest a time series with observations for a metering point.
/// If an existing time series covers the same period, it becomes a new version
/// (the old one is marked as superseded), which triggers correction detection.
/// </summary>
app.MapPost("/api/time-series", async (CreateTimeSeriesRequest req, WattsOnDbContext db) =>
{
    // Validate metering point exists
    var mp = await db.MeteringPoints.FindAsync(req.MeteringPointId);
    if (mp is null) return Results.BadRequest(new { error = "MeteringPoint not found" });

    var period = Period.Create(req.PeriodStart, req.PeriodEnd);
    var resolution = Enum.Parse<Resolution>(req.Resolution);

    // Check for existing time series for the same period → new version
    var existing = await db.TimeSeriesCollection
        .Where(t => t.MeteringPointId == req.MeteringPointId)
        .Where(t => t.Period.Start == req.PeriodStart && t.Period.End == req.PeriodEnd)
        .Where(t => t.IsLatest)
        .FirstOrDefaultAsync();

    var version = 1;
    if (existing is not null)
    {
        existing.Supersede();
        version = existing.Version + 1;
    }

    var time_series = TimeSeries.Create(req.MeteringPointId, period, resolution, version, req.TransactionId);

    foreach (var obs in req.Observations)
    {
        var quality = Enum.Parse<QuantityQuality>(obs.Quality ?? "Målt");
        time_series.AddObservation(obs.Timestamp, EnergyQuantity.Create(obs.KWh), quality);
    }

    db.TimeSeriesCollection.Add(time_series);
    await db.SaveChangesAsync();

    return Results.Created($"/api/time_series/{time_series.Id}", new
    {
        time_series.Id,
        time_series.MeteringPointId,
        PeriodStart = time_series.Period.Start,
        PeriodEnd = time_series.Period.End,
        Resolution = time_series.Resolution.ToString(),
        time_series.Version,
        time_series.IsLatest,
        ObservationCount = time_series.Observations.Count,
        TotalEnergyKwh = time_series.TotalEnergy.Value
    });
}).WithName("CreateTimeSeries");

app.MapGet("/api/time_series/{id:guid}", async (Guid id, WattsOnDbContext db) =>
{
    var ts = await db.TimeSeriesCollection
        .Include(t => t.Observations.OrderBy(o => o.Timestamp))
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == id);

    if (ts is null) return Results.NotFound();

    return Results.Ok(new
    {
        ts.Id,
        ts.MeteringPointId,
        PeriodStart = ts.Period.Start,
        PeriodEnd = ts.Period.End,
        Resolution = ts.Resolution.ToString(),
        ts.Version,
        ts.IsLatest,
        ts.TransactionId,
        ts.ReceivedAt,
        TotalEnergyKwh = ts.TotalEnergy.Value,
        Observations = ts.Observations.Select(o => new
        {
            o.Timestamp,
            KWh = o.Quantity.Value,
            Quality = o.Quality.ToString()
        })
    });
}).WithName("GetTimeSeries");

// ==================== SETTLEMENT DOCUMENTS (Peppol-aligned pre-invoice) ====================

/// <summary>
/// Settlement documents ready for external invoicing system.
/// Returns Peppol BIS-aligned pre-invoice documents with VAT, buyer/seller, and line items.
/// Query by status: ready (new settlements), all, or corrections.
/// </summary>
app.MapGet("/api/settlement-documents", async (string? status, WattsOnDbContext db) =>
{
    var query = db.Settlements
        .Include(a => a.Lines)
        .Include(a => a.MeteringPoint)
        .Include(a => a.Supply)
            .ThenInclude(l => l.Customer)
                .ThenInclude(c => c.SupplierIdentity)
        .AsNoTracking();

    // Filter: "ready" = Beregnet (default), "all" = everything
    if (status == "all")
    {
        // No filter
    }
    else if (status == "corrections")
    {
        query = query.Where(a => a.IsCorrection && a.Status == SettlementStatus.Calculated);
    }
    else // "ready" (default)
    {
        query = query.Where(a => a.Status == SettlementStatus.Calculated);
    }

    var settlements = await query.OrderBy(a => a.DocumentNumber).ToListAsync();

    // Load price VAT info for all referenced prices
    var priceIds = settlements.SelectMany(a => a.Lines).Select(l => l.PriceId).Distinct().ToList();
    var prisVatMap = await db.Prices
        .Where(p => priceIds.Contains(p.Id))
        .AsNoTracking()
        .ToDictionaryAsync(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

    const decimal DanishVatRate = 25.0m;

    var documents = settlements.Select(a =>
    {
        var customer = a.Supply.Customer;
        var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
        var isDebit = a.IsCorrection && a.TotalAmount.Amount >= 0;

        var documentType = a.IsCorrection
            ? (isCredit ? "creditNote" : "debitNote")
            : "settlement";

        var year = a.CalculatedAt.Year;
        var documentId = $"WO-{year}-{a.DocumentNumber:D5}";
        string? originalDocumentId = null;
        if (a.PreviousSettlementId.HasValue)
        {
            var original = settlements.FirstOrDefault(s => s.Id == a.PreviousSettlementId);
            if (original is not null)
                originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
        }

        var lines = a.Lines.Select((line, idx) =>
        {
            var prisInfo = prisVatMap.GetValueOrDefault(line.PriceId);
            var vatExempt = prisInfo?.VatExempt ?? false;
            var taxPercent = vatExempt ? 0m : DanishVatRate;
            var taxAmount = vatExempt ? 0m : Math.Round(line.Amount.Amount * taxPercent / 100m, 2);

            return new
            {
                lineNumber = idx + 1,
                description = line.Description,
                quantity = line.Quantity.Value,
                quantityUnit = "KWH",
                unitPrice = line.UnitPrice,
                lineAmount = line.Amount.Amount,
                chargeId = prisInfo?.ChargeId,
                chargeOwnerGln = prisInfo?.OwnerGln,
                taxCategory = vatExempt ? "Z" : "S", // S = Standard, Z = Zero-rated
                taxPercent = taxPercent,
                taxAmount = taxAmount
            };
        }).ToList();

        var totalExclVat = a.TotalAmount.Amount;
        var totalVat = lines.Sum(l => l.taxAmount);
        var totalInclVat = totalExclVat + totalVat;

        // Group tax summary by category+rate
        var taxSummary = lines
            .GroupBy(l => new { l.taxCategory, l.taxPercent })
            .Select(g => new
            {
                taxCategory = g.Key.taxCategory,
                taxPercent = g.Key.taxPercent,
                taxableAmount = g.Sum(l => l.lineAmount),
                taxAmount = g.Sum(l => l.taxAmount)
            })
            .ToList();

        return new
        {
            documentType,
            documentId,
            originalDocumentId,
            settlementId = a.Id,
            status = a.Status.ToString(),

            period = new { start = a.SettlementPeriod.Start, end = a.SettlementPeriod.End },

            seller = new
            {
                name = a.Supply.Customer.SupplierIdentity.Name,
                identifier = a.Supply.Customer.SupplierIdentity.Cvr?.Value,
                identifierScheme = "DK:CVR",
                glnNumber = a.Supply.Customer.SupplierIdentity.Gln.Value
            },
            buyer = new
            {
                name = customer.Name,
                identifier = customer.IsPrivate ? customer.Cpr?.Value : customer.Cvr?.Value,
                identifierScheme = customer.IsPrivate ? "DK:CPR" : "DK:CVR",
                address = customer.Address != null ? new
                {
                    streetName = customer.Address.StreetName,
                    buildingNumber = customer.Address.BuildingNumber,
                    floor = customer.Address.Floor,
                    suite = customer.Address.Suite,
                    postCode = customer.Address.PostCode,
                    cityName = customer.Address.CityName
                } : (object?)null
            },

            meteringPoint = new
            {
                gsrn = a.MeteringPoint.Gsrn.Value,
                gridArea = a.MeteringPoint.GridArea
            },

            lines,
            taxSummary,

            totalExclVat,
            totalVat,
            totalInclVat,
            currency = a.TotalAmount.Currency,

            calculatedAt = a.CalculatedAt,
            externalInvoiceReference = a.ExternalInvoiceReference,
            invoicedAt = a.InvoicedAt
        };
    }).ToList();

    return Results.Ok(documents);
}).WithName("GetSettlementDocuments");

/// <summary>
/// Get a single settlement document by ID.
/// </summary>
app.MapGet("/api/settlement-documents/{id:guid}", async (Guid id, WattsOnDbContext db) =>
{
    var a = await db.Settlements
        .Include(a => a.Lines)
        .Include(a => a.MeteringPoint)
        .Include(a => a.Supply)
            .ThenInclude(l => l.Customer)
                .ThenInclude(c => c.SupplierIdentity)
        .AsNoTracking()
        .FirstOrDefaultAsync(a => a.Id == id);

    if (a is null) return Results.NotFound();

    var priceIds = a.Lines.Select(l => l.PriceId).Distinct().ToList();
    var prisVatMap = await db.Prices
        .Where(p => priceIds.Contains(p.Id))
        .AsNoTracking()
        .ToDictionaryAsync(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

    const decimal DanishVatRate = 25.0m;

    var customer = a.Supply.Customer;
    var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
    var documentType = a.IsCorrection
        ? (isCredit ? "creditNote" : "debitNote")
        : "settlement";

    var year = a.CalculatedAt.Year;
    var documentId = $"WO-{year}-{a.DocumentNumber:D5}";

    string? originalDocumentId = null;
    if (a.PreviousSettlementId.HasValue)
    {
        var original = await db.Settlements.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == a.PreviousSettlementId);
        if (original is not null)
            originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
    }

    var lines = a.Lines.Select((line, idx) =>
    {
        var prisInfo = prisVatMap.GetValueOrDefault(line.PriceId);
        var vatExempt = prisInfo?.VatExempt ?? false;
        var taxPercent = vatExempt ? 0m : DanishVatRate;
        var taxAmount = vatExempt ? 0m : Math.Round(line.Amount.Amount * taxPercent / 100m, 2);

        return new
        {
            lineNumber = idx + 1,
            description = line.Description,
            quantity = line.Quantity.Value,
            quantityUnit = "KWH",
            unitPrice = line.UnitPrice,
            lineAmount = line.Amount.Amount,
            chargeId = prisInfo?.ChargeId,
            chargeOwnerGln = prisInfo?.OwnerGln,
            taxCategory = vatExempt ? "Z" : "S",
            taxPercent,
            taxAmount
        };
    }).ToList();

    var totalExclVat = a.TotalAmount.Amount;
    var totalVat = lines.Sum(l => l.taxAmount);
    var totalInclVat = totalExclVat + totalVat;

    var taxSummary = lines
        .GroupBy(l => new { l.taxCategory, l.taxPercent })
        .Select(g => new
        {
            taxCategory = g.Key.taxCategory,
            taxPercent = g.Key.taxPercent,
            taxableAmount = g.Sum(l => l.lineAmount),
            taxAmount = g.Sum(l => l.taxAmount)
        })
        .ToList();

    return Results.Ok(new
    {
        documentType,
        documentId,
        originalDocumentId,
        settlementId = a.Id,
        status = a.Status.ToString(),

        period = new { start = a.SettlementPeriod.Start, end = a.SettlementPeriod.End },

        seller = new
        {
            name = a.Supply.Customer.SupplierIdentity.Name,
            identifier = a.Supply.Customer.SupplierIdentity.Cvr?.Value,
            identifierScheme = "DK:CVR",
            glnNumber = a.Supply.Customer.SupplierIdentity.Gln.Value
        },
        buyer = new
        {
            name = customer.Name,
            identifier = customer.IsPrivate ? customer.Cpr?.Value : customer.Cvr?.Value,
            identifierScheme = customer.IsPrivate ? "DK:CPR" : "DK:CVR",
            address = customer.Address != null ? new
            {
                streetName = customer.Address.StreetName,
                buildingNumber = customer.Address.BuildingNumber,
                floor = customer.Address.Floor,
                suite = customer.Address.Suite,
                postCode = customer.Address.PostCode,
                cityName = customer.Address.CityName
            } : (object?)null
        },

        meteringPoint = new
        {
            gsrn = a.MeteringPoint.Gsrn.Value,
            gridArea = a.MeteringPoint.GridArea
        },

        lines,
        taxSummary,

        totalExclVat,
        totalVat,
        totalInclVat,
        currency = a.TotalAmount.Currency,

        calculatedAt = a.CalculatedAt,
        externalInvoiceReference = a.ExternalInvoiceReference,
        invoicedAt = a.InvoicedAt
    });
}).WithName("GetSettlementDocument");

/// <summary>
/// External system confirms a settlement document has been invoiced.
/// Accepts the WattsOn document ID or settlement GUID.
/// </summary>
app.MapPost("/api/settlement-documents/{id:guid}/confirm", async (Guid id, ConfirmSettlementRequest req, WattsOnDbContext db) =>
{
    var settlement = await db.Settlements.FindAsync(id);
    if (settlement is null) return Results.NotFound();

    try
    {
        settlement.MarkInvoiced(req.ExternalInvoiceReference);
        await db.SaveChangesAsync();

        var year = settlement.CalculatedAt.Year;
        return Results.Ok(new
        {
            documentId = $"WO-{year}-{settlement.DocumentNumber:D5}",
            settlementId = settlement.Id,
            status = settlement.Status.ToString(),
            externalInvoiceReference = settlement.ExternalInvoiceReference,
            invoicedAt = settlement.InvoicedAt
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
}).WithName("ConfirmSettlementDocument");

// ==================== SIMULATION ====================

/// <summary>
/// Simulate a full BRS-001 supplier change.
/// Creates all necessary entities (customer, metering point if needed) and
/// runs the entire process flow including DataHub message simulation.
/// This is the "no seed data" approach — the system proves itself.
/// </summary>
app.MapPost("/api/simulation/supplier-change", async (SimulateSupplierChangeRequest req, WattsOnDbContext db) =>
{
    var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
    if (identity is null) return Results.Problem("No supplier identity configured");

    // 1. Find or create the metering point
    var gsrn = Gsrn.Create(req.Gsrn);
    var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == req.Gsrn);
    if (mp is null)
    {
        var gridGln = GlnNumber.Create(req.GridCompanyGln ?? "5790000610976");
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        mp = MeteringPoint.Create(gsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, req.GridArea ?? "DK1", gridGln, address);
        db.MeteringPoints.Add(mp);
    }

    // 2. Find or create the customer
    Customer? customer = null;
    if (req.CprNumber != null)
        customer = await db.Customers.FirstOrDefaultAsync(k => k.Cpr != null && k.Cpr.Value == req.CprNumber);
    else if (req.CvrNumber != null)
        customer = await db.Customers.FirstOrDefaultAsync(k => k.Cvr != null && k.Cvr.Value == req.CvrNumber);

    if (customer is null)
    {
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        customer = req.CprNumber != null
            ? Customer.CreatePerson(req.CustomerName, CprNumber.Create(req.CprNumber), identity.Id, address)
            : Customer.CreateCompany(req.CustomerName, CvrNumber.Create(req.CvrNumber!), identity.Id, address);
        if (req.Email != null || req.Phone != null)
            customer.UpdateContactInfo(req.Email, req.Phone);
        db.Customers.Add(customer);
    }

    // 3. Check for existing supply (if another supplier had this customer)
    var currentSupply = await db.Supplies
        .Where(l => l.MeteringPointId == mp.Id)
        .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > req.EffectiveDate)
        .FirstOrDefaultAsync();

    // 4. Create the BRS-001 process
    var process = Brs001Handler.InitiateSupplierChange(
        gsrn,
        req.EffectiveDate,
        req.CprNumber,
        req.CvrNumber,
        GlnNumber.Create(req.PreviousSupplierGln ?? "5790000000005"));

    // 5. Simulate DataHub confirmation
    var transactionId = $"DH-SIM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
    Brs001Handler.HandleConfirmation(process, transactionId);

    // 6. Execute the supplier change
    var result = Brs001Handler.ExecuteSupplierChange(
        process, mp, customer, currentSupply);

    mp.SetActiveSupply(true);

    // 7. Create simulated inbox messages (audit trail)
    var requestMsg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-001", "5790000432752", identity.Gln.Value,
        System.Text.Json.JsonSerializer.Serialize(new
        {
            businessReason = "E03",
            gsrn = req.Gsrn,
            effectiveDate = req.EffectiveDate,
            cpr = req.CprNumber,
            cvr = req.CvrNumber,
            transactionId
        }),
        "BRS-001");
    requestMsg.MarkProcessed(process.Id);

    var confirmMsg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-001", "5790000432752", identity.Gln.Value,
        System.Text.Json.JsonSerializer.Serialize(new
        {
            businessReason = "E03",
            confirmation = true,
            transactionId,
            gsrn = req.Gsrn
        }),
        "BRS-001");
    confirmMsg.MarkProcessed(process.Id);

    // 8. Link standard prices to the metering point (if not already linked)
    var existingLinks = await db.PriceLinks
        .Where(pt => pt.MeteringPointId == mp.Id)
        .CountAsync();

    var newPriceLinks = 0;
    if (existingLinks == 0)
    {
        var allPrices = await db.Prices.ToListAsync();
        foreach (var pris in allPrices)
        {
            var link = PriceLink.Create(mp.Id, pris.Id, Period.From(req.EffectiveDate));
            db.PriceLinks.Add(link);
            newPriceLinks++;
        }
    }

    // 9. Generate simulated time series with consumption data
    TimeSeries? time_series = null;
    if (req.GenerateConsumption)
    {
        var periodStart = req.EffectiveDate;
        // Generate one month of data from effective date
        var periodEnd = new DateTimeOffset(
            periodStart.Year, periodStart.Month, 1, 0, 0, 0, periodStart.Offset)
            .AddMonths(1);

        time_series = TimeSeries.Create(mp.Id, Period.Create(periodStart, periodEnd),
            Resolution.PT1H, 1, $"SIM-{transactionId}");

        var hours = (int)(periodEnd - periodStart).TotalHours;
        var rng = new Random(req.Gsrn.GetHashCode()); // Deterministic per GSRN
        for (int i = 0; i < hours; i++)
        {
            var ts = periodStart.AddHours(i);
            var hour = ts.Hour;
            // Realistic Danish household pattern
            var baseKwh = hour switch
            {
                >= 23 or <= 5 => 0.3m + (decimal)(rng.NextDouble() * 0.4),   // Night: 0.3-0.7
                >= 6 and <= 8 => 0.8m + (decimal)(rng.NextDouble() * 0.8),    // Morning: 0.8-1.6
                >= 9 and <= 15 => 0.5m + (decimal)(rng.NextDouble() * 0.6),   // Day: 0.5-1.1
                >= 16 and <= 19 => 1.2m + (decimal)(rng.NextDouble() * 1.2),  // Evening peak: 1.2-2.4
                _ => 0.6m + (decimal)(rng.NextDouble() * 0.8),                // Late evening: 0.6-1.4
            };
            time_series.AddObservation(ts, EnergyQuantity.Create(baseKwh), QuantityQuality.Measured);
        }
        db.TimeSeriesCollection.Add(time_series);
    }

    // Save everything
    db.Processes.Add(process);
    if (result.NewSupply != null) db.Supplies.Add(result.NewSupply);
    db.InboxMessages.Add(requestMsg);
    db.InboxMessages.Add(confirmMsg);
    await db.SaveChangesAsync();

    return Results.Created($"/api/processer/{process.Id}", new
    {
        processId = process.Id,
        transactionId,
        status = process.Status.ToString(),
        currentState = process.CurrentState,
        gsrn = req.Gsrn,
        customerName = customer.Name,
        customerId = customer.Id,
        meteringPointId = mp.Id,
        newSupplyId = result.NewSupply?.Id,
        endedSupplyId = result.EndedSupply?.Id,
        effectiveDate = req.EffectiveDate,
        priceLinksCreated = newPriceLinks,
        timeSeriesGenerated = time_series != null,
        totalEnergyKwh = time_series?.TotalEnergy.Value,
        message = $"Leverandørskift gennemført for {customer.Name} på {req.Gsrn}. " +
                  $"Effektiv dato: {req.EffectiveDate:yyyy-MM-dd}." +
                  (time_series != null ? $" Genereret {time_series.Observations.Count} timer forbrugsdata ({time_series.TotalEnergy.Value:F1} kWh)." : "") +
                  (newPriceLinks > 0 ? $" {newPriceLinks} prices tilknyttet." : "") +
                  " SettlementWorker beregner automatisk settlement inden for 30 secustomers."
    });
}).WithName("SimulateSupplierChange");

/// <summary>
/// BRS-001 Recipient: Lose a customer — another supplier takes over.
/// Ends the supply and triggers final settlement.
/// </summary>
app.MapPost("/api/simulation/supplier-change-outgoing", async (SimulateOutgoingSupplierChangeRequest req, WattsOnDbContext db) =>
{
    // Find the supply
    var supply = await db.Supplies
        .Include(l => l.Customer)
        .Include(l => l.MeteringPoint)
        .Where(l => l.Id == req.SupplyId)
        .FirstOrDefaultAsync();

    if (supply is null)
        return Results.NotFound("Supply ikke fundet");

    if (!supply.IsActive)
        return Results.Problem("Supply er allerede afsluttet");

    var gsrn = supply.MeteringPoint.Gsrn;
    var newSupplierGln = GlnNumber.Create(req.NewSupplierGln ?? "5790000000005");

    var result = Brs001Handler.HandleAsRecipient(
        gsrn, req.EffectiveDate,
        $"DH-SIM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
        newSupplierGln, supply);

    supply.MeteringPoint.SetActiveSupply(false);

    // Create audit trail inbox message
    var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
    var msg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-004", "5790000432752", identity?.Gln.Value ?? "unknown",
        System.Text.Json.JsonSerializer.Serialize(new
        {
            businessReason = "E03",
            stopOfSupply = true,
            gsrn = gsrn.Value,
            effectiveDate = req.EffectiveDate,
            newSupplierGln = newSupplierGln.Value,
        }),
        "BRS-001");
    msg.MarkProcessed(result.Process.Id);

    db.Processes.Add(result.Process);
    db.InboxMessages.Add(msg);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        processId = result.Process.Id,
        transactionId = result.Process.TransactionId,
        status = result.Process.Status.ToString(),
        currentState = result.Process.CurrentState,
        gsrn = gsrn.Value,
        customerName = supply.Customer.Name,
        customerId = supply.Customer.Id,
        endedSupplyId = result.EndedSupply?.Id,
        effectiveDate = req.EffectiveDate,
        message = $"Leverandørskift udgående — {supply.Customer.Name} forlader os på {gsrn.Value}. " +
                  $"Supply afsluttet pr. {req.EffectiveDate:yyyy-MM-dd}. Slutsettlement beregnes automatisk."
    });
}).WithName("SimulateOutgoingSupplierChange");

/// <summary>
/// BRS-009: Move-in — new customer at a metering point.
/// </summary>
app.MapPost("/api/simulation/move-in", async (SimulateMoveInRequest req, WattsOnDbContext db) =>
{
    var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
    if (identity is null) return Results.Problem("No supplier identity configured");

    // Find or create metering point
    var gsrn = Gsrn.Create(req.Gsrn);
    var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == req.Gsrn);
    if (mp is null)
    {
        var gridGln = GlnNumber.Create(req.GridCompanyGln ?? "5790000610976");
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        mp = MeteringPoint.Create(gsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, req.GridArea ?? "DK1", gridGln, address);
        db.MeteringPoints.Add(mp);
    }

    // Find or create customer
    Customer? customer = null;
    if (req.CprNumber != null)
        customer = await db.Customers.FirstOrDefaultAsync(k => k.Cpr != null && k.Cpr.Value == req.CprNumber);
    else if (req.CvrNumber != null)
        customer = await db.Customers.FirstOrDefaultAsync(k => k.Cvr != null && k.Cvr.Value == req.CvrNumber);

    if (customer is null)
    {
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        customer = req.CprNumber != null
            ? Customer.CreatePerson(req.CustomerName, CprNumber.Create(req.CprNumber), identity.Id, address)
            : Customer.CreateCompany(req.CustomerName, CvrNumber.Create(req.CvrNumber!), identity.Id, address);
        if (req.Email != null || req.Phone != null)
            customer.UpdateContactInfo(req.Email, req.Phone);
        db.Customers.Add(customer);
    }

    // Check for existing supply (previous tenant)
    var currentSupply = await db.Supplies
        .Where(l => l.MeteringPointId == mp.Id)
        .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > req.EffectiveDate)
        .FirstOrDefaultAsync();

    var result = Brs009Handler.ExecuteMoveIn(
        gsrn, req.EffectiveDate, req.CprNumber, req.CvrNumber,
        mp, customer, currentSupply);

    mp.SetActiveSupply(true);

    // Link prices
    var existingLinks = await db.PriceLinks
        .Where(pt => pt.MeteringPointId == mp.Id).CountAsync();
    var newPriceLinks = 0;
    if (existingLinks == 0)
    {
        foreach (var pris in await db.Prices.ToListAsync())
        {
            db.PriceLinks.Add(PriceLink.Create(mp.Id, pris.Id, Period.From(req.EffectiveDate)));
            newPriceLinks++;
        }
    }

    // Generate consumption data
    TimeSeries? time_series = null;
    if (req.GenerateConsumption)
    {
        var periodStart = req.EffectiveDate;
        var periodEnd = new DateTimeOffset(
            periodStart.Year, periodStart.Month, 1, 0, 0, 0, periodStart.Offset).AddMonths(1);

        time_series = TimeSeries.Create(mp.Id, Period.Create(periodStart, periodEnd),
            Resolution.PT1H, 1, $"SIM-{result.Process.TransactionId}");

        var hours = (int)(periodEnd - periodStart).TotalHours;
        var rng = new Random(req.Gsrn.GetHashCode());
        for (int i = 0; i < hours; i++)
        {
            var ts = periodStart.AddHours(i);
            var hour = ts.Hour;
            var baseKwh = hour switch
            {
                >= 23 or <= 5 => 0.3m + (decimal)(rng.NextDouble() * 0.4),
                >= 6 and <= 8 => 0.8m + (decimal)(rng.NextDouble() * 0.8),
                >= 9 and <= 15 => 0.5m + (decimal)(rng.NextDouble() * 0.6),
                >= 16 and <= 19 => 1.2m + (decimal)(rng.NextDouble() * 1.2),
                _ => 0.6m + (decimal)(rng.NextDouble() * 0.8),
            };
            time_series.AddObservation(ts, EnergyQuantity.Create(baseKwh), QuantityQuality.Measured);
        }
        db.TimeSeriesCollection.Add(time_series);
    }

    // Audit trail
    var inboxMsg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-001", "5790000432752", identity.Gln.Value,
        System.Text.Json.JsonSerializer.Serialize(new
        {
            businessReason = "E65",
            gsrn = req.Gsrn,
            effectiveDate = req.EffectiveDate,
            cpr = req.CprNumber,
            cvr = req.CvrNumber,
        }),
        "BRS-009");
    inboxMsg.MarkProcessed(result.Process.Id);

    db.Processes.Add(result.Process);
    db.Supplies.Add(result.NewSupply);
    db.InboxMessages.Add(inboxMsg);
    await db.SaveChangesAsync();

    return Results.Created($"/api/processer/{result.Process.Id}", new
    {
        processId = result.Process.Id,
        transactionId = result.Process.TransactionId,
        status = result.Process.Status.ToString(),
        currentState = result.Process.CurrentState,
        gsrn = req.Gsrn,
        customerName = customer.Name,
        customerId = customer.Id,
        newSupplyId = result.NewSupply.Id,
        endedSupplyId = result.EndedSupply?.Id,
        previousCustomerName = result.EndedSupply != null ? "Tidligere lejer" : null,
        effectiveDate = req.EffectiveDate,
        priceLinksCreated = newPriceLinks,
        timeSeriesGenerated = time_series != null,
        totalEnergyKwh = time_series?.TotalEnergy.Value,
        message = $"Tilflytning gennemført for {customer.Name} på {req.Gsrn}. " +
                  $"Effektiv dato: {req.EffectiveDate:yyyy-MM-dd}." +
                  (result.EndedSupply != null ? " Tidligere supply afsluttet." : "") +
                  (time_series != null ? $" Genereret {time_series.Observations.Count} timer forbrugsdata ({time_series.TotalEnergy.Value:F1} kWh)." : "") +
                  (newPriceLinks > 0 ? $" {newPriceLinks} prices tilknyttet." : "") +
                  " SettlementWorker beregner automatisk settlement inden for 30 secustomers."
    });
}).WithName("SimulateMoveIn");

/// <summary>
/// Move-out: Customer leaves a metering point. Ends supply, final settlement.
/// </summary>
app.MapPost("/api/simulation/move-out", async (SimulateMoveOutRequest req, WattsOnDbContext db) =>
{
    var supply = await db.Supplies
        .Include(l => l.Customer)
        .Include(l => l.MeteringPoint)
        .Where(l => l.Id == req.SupplyId)
        .FirstOrDefaultAsync();

    if (supply is null)
        return Results.NotFound("Supply ikke fundet");

    if (!supply.IsActive)
        return Results.Problem("Supply er allerede afsluttet");

    var gsrn = supply.MeteringPoint.Gsrn;
    var result = Brs009Handler.ExecuteMoveOut(gsrn, req.EffectiveDate, supply);

    supply.MeteringPoint.SetActiveSupply(false);

    // Audit trail
    var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
    var msg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-001", "5790000432752", identity?.Gln.Value ?? "unknown",
        System.Text.Json.JsonSerializer.Serialize(new
        {
            businessReason = "E01",
            gsrn = gsrn.Value,
            effectiveDate = req.EffectiveDate,
            moveOut = true,
        }),
        "BRS-010");
    msg.MarkProcessed(result.Process.Id);

    db.Processes.Add(result.Process);
    db.InboxMessages.Add(msg);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        processId = result.Process.Id,
        transactionId = result.Process.TransactionId,
        status = result.Process.Status.ToString(),
        currentState = result.Process.CurrentState,
        gsrn = gsrn.Value,
        customerName = supply.Customer.Name,
        customerId = supply.Customer.Id,
        endedSupplyId = result.EndedSupply.Id,
        effectiveDate = req.EffectiveDate,
        message = $"Fraflytning gennemført — {supply.Customer.Name} fraflyttet {gsrn.Value}. " +
                  $"Supply afsluttet pr. {req.EffectiveDate:yyyy-MM-dd}. Slutsettlement beregnes automatisk."
    });
}).WithName("SimulateMoveOut");

// ==================== DASHBOARD ====================

app.MapGet("/api/dashboard", async (WattsOnDbContext db) =>
{
    var customerCount = await db.Customers.CountAsync();
    var mpCount = await db.MeteringPoints.CountAsync();
    var activeSupplies = await db.Supplies.CountAsync(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > DateTimeOffset.UtcNow);
    var activeProcesses = await db.Processes.CountAsync(p => p.CompletedAt == null);
    var unprocessedInbox = await db.InboxMessages.CountAsync(m => !m.IsProcessed);
    var unsentOutbox = await db.OutboxMessages.CountAsync(m => !m.IsSent);

    // Settlement stats
    var beregnede = await db.Settlements.CountAsync(a => a.Status == SettlementStatus.Calculated && !a.IsCorrection);
    var fakturerede = await db.Settlements.CountAsync(a => a.Status == SettlementStatus.Invoiced);
    var justerede = await db.Settlements.CountAsync(a => a.Status == SettlementStatus.Adjusted);
    var korrektioner = await db.Settlements.CountAsync(a => a.IsCorrection && a.Status == SettlementStatus.Calculated);
    var totalSettlementAmount = await db.Settlements
        .Where(a => !a.IsCorrection)
        .SumAsync(a => a.TotalAmount.Amount);

    return Results.Ok(new
    {
        customers = customerCount,
        meteringPoints = mpCount,
        activeSupplies = activeSupplies,
        activeProcesses = activeProcesses,
        unprocessedInbox = unprocessedInbox,
        unsentOutbox = unsentOutbox,
        settlements = new
        {
            calculated = beregnede,
            invoiced = fakturerede,
            adjusted = justerede,
            corrections = korrektioner,
            totalAmount = totalSettlementAmount,
        }
    });
}).WithName("GetDashboard");

// ==================== SPOT PRICES ====================

app.MapGet("/api/spot-prices", async (string? area, int? days, WattsOnDbContext db) =>
{
    var since = DateTimeOffset.UtcNow.AddDays(-(days ?? 7));
    var query = db.SpotPrices
        .Where(sp => sp.HourUtc >= since)
        .OrderByDescending(sp => sp.HourUtc)
        .AsQueryable();

    if (!string.IsNullOrEmpty(area))
        query = query.Where(sp => sp.PriceArea == area);

    var prices = await query.Take(1000).Select(sp => new
    {
        sp.HourUtc,
        sp.HourDk,
        sp.PriceArea,
        sp.SpotPriceDkkPerMwh,
        sp.SpotPriceEurPerMwh,
        spotPriceDkkPerKwh = sp.SpotPriceDkkPerMwh / 1000m
    }).ToListAsync();

    return Results.Ok(prices);
}).WithName("GetSpotPrices");

app.MapGet("/api/spot-prices/latest", async (WattsOnDbContext db) =>
{
    // Get the latest price for each area
    var dk1 = await db.SpotPrices
        .Where(sp => sp.PriceArea == "DK1")
        .OrderByDescending(sp => sp.HourUtc)
        .FirstOrDefaultAsync();

    var dk2 = await db.SpotPrices
        .Where(sp => sp.PriceArea == "DK2")
        .OrderByDescending(sp => sp.HourUtc)
        .FirstOrDefaultAsync();

    var totalCount = await db.SpotPrices.CountAsync();

    return Results.Ok(new
    {
        totalRecords = totalCount,
        dk1 = dk1 == null ? null : new
        {
            dk1.HourUtc,
            dk1.HourDk,
            dk1.SpotPriceDkkPerMwh,
            dk1.SpotPriceEurPerMwh,
            spotPriceDkkPerKwh = dk1.SpotPriceDkkPerMwh / 1000m
        },
        dk2 = dk2 == null ? null : new
        {
            dk2.HourUtc,
            dk2.HourDk,
            dk2.SpotPriceDkkPerMwh,
            dk2.SpotPriceEurPerMwh,
            spotPriceDkkPerKwh = dk2.SpotPriceDkkPerMwh / 1000m
        }
    });
}).WithName("GetLatestSpotPrices");

app.Run();

// ==================== REQUEST DTOs ====================

record CreateSupplierIdentityRequest(string Gln, string Name, string? Cvr, bool IsActive = true);

record PatchSupplierIdentityRequest(bool? IsActive = null, string? Name = null, string? Cvr = null);

record AddressDto(string StreetName, string BuildingNumber, string PostCode, string CityName,
    string? Floor = null, string? Suite = null);

record CreateCustomerRequest(string Name, string? Cpr, string? Cvr, Guid SupplierIdentityId, string? Email, string? Phone, AddressDto? Address);

record CreateMeteringPointRequest(string Gsrn, string Type, string Art, string SettlementMethod,
    string Resolution, string GridArea, string GridCompanyGln, AddressDto? Address);

record CreateSupplyRequest(Guid MeteringPointId, Guid CustomerId,
    DateTimeOffset SupplyStart, DateTimeOffset? SupplyEnd);

record MarkInvoicedRequest(string ExternalInvoiceReference);

record PricePointDto(DateTimeOffset Timestamp, decimal Price);

record CreatePrisRequest(
    string ChargeId,
    string OwnerGln,
    string Type,
    string Description,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    bool VatExempt = false,
    string? PriceResolution = null,
    List<PricePointDto>? PricePoints = null);

record CreatePriceLinkRequest(
    Guid MeteringPointId,
    Guid PriceId,
    DateTimeOffset LinkFrom,
    DateTimeOffset? LinkTo);

record ConfirmSettlementRequest(string ExternalInvoiceReference);

record SimulateSupplierChangeRequest(
    string Gsrn,
    DateTimeOffset EffectiveDate,
    string CustomerName,
    string? CprNumber = null,
    string? CvrNumber = null,
    string? Email = null,
    string? Phone = null,
    AddressDto? Address = null,
    string? PreviousSupplierGln = null,
    string? GridCompanyGln = null,
    string? GridArea = null,
    bool GenerateConsumption = true);

record SimulateOutgoingSupplierChangeRequest(
    Guid SupplyId,
    DateTimeOffset EffectiveDate,
    string? NewSupplierGln = null);

record SimulateMoveInRequest(
    string Gsrn,
    DateTimeOffset EffectiveDate,
    string CustomerName,
    string? CprNumber = null,
    string? CvrNumber = null,
    string? Email = null,
    string? Phone = null,
    AddressDto? Address = null,
    string? GridCompanyGln = null,
    string? GridArea = null,
    bool GenerateConsumption = true);

record SimulateMoveOutRequest(
    Guid SupplyId,
    DateTimeOffset EffectiveDate);

record CreateTimeSeriesRequest(
    Guid MeteringPointId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Resolution,
    string? TransactionId,
    List<ObservationDto> Observations);

record ObservationDto(DateTimeOffset Timestamp, decimal KWh, string? Quality);

record EndOfSupplyRequest(string Gsrn, DateTimeOffset DesiredEndDate, string? Reason);

record MoveOutRequest(string Gsrn, DateTimeOffset EffectiveDate);

record CustomerUpdateRequest(
    string Gsrn,
    DateTimeOffset EffectiveDate,
    string CustomerName,
    string? Cpr,
    string? Cvr,
    string? Email,
    string? Phone,
    AddressDto? Address);
