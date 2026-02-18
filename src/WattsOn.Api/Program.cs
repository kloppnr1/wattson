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

// Auto-migrate on startup (dev only)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();
    await db.Database.MigrateAsync();
    app.MapOpenApi();
}

app.UseCors();

// --- API Endpoints ---

app.MapGet("/api/health", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }))
    .WithName("Health");

// ==================== AKTØRER ====================

app.MapGet("/api/aktører", async (WattsOnDbContext db) =>
{
    var aktører = await db.Aktører
        .AsNoTracking()
        .OrderBy(a => a.Name)
        .Select(a => new
        {
            a.Id,
            Gln = a.Gln.Value,
            a.Name,
            Role = a.Role.ToString(),
            Cvr = a.Cvr != null ? a.Cvr.Value : null,
            a.IsOwn,
            a.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(aktører);
}).WithName("GetAktører");

app.MapPost("/api/aktører", async (CreateAktørRequest req, WattsOnDbContext db) =>
{
    var gln = GlnNumber.Create(req.Gln);
    var cvr = req.Cvr != null ? CvrNumber.Create(req.Cvr) : null;
    var role = Enum.Parse<ActorRole>(req.Role);

    var aktør = req.IsOwn
        ? Aktør.CreateOwn(gln, req.Name, cvr ?? throw new ArgumentException("CVR required for own actor"))
        : Aktør.Create(gln, req.Name, role, cvr);

    db.Aktører.Add(aktør);
    await db.SaveChangesAsync();

    return Results.Created($"/api/aktører/{aktør.Id}", new { aktør.Id, Gln = aktør.Gln.Value, aktør.Name });
}).WithName("CreateAktør");

// ==================== KUNDER ====================

app.MapGet("/api/kunder", async (WattsOnDbContext db) =>
{
    var kunder = await db.Kunder
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
            k.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(kunder);
}).WithName("GetKunder");

app.MapGet("/api/kunder/{id:guid}", async (Guid id, WattsOnDbContext db) =>
{
    var kunde = await db.Kunder
        .Include(k => k.Leverancer)
            .ThenInclude(l => l.Målepunkt)
        .AsNoTracking()
        .FirstOrDefaultAsync(k => k.Id == id);

    if (kunde is null) return Results.NotFound();

    return Results.Ok(new
    {
        kunde.Id,
        kunde.Name,
        Cpr = kunde.Cpr?.Value,
        Cvr = kunde.Cvr?.Value,
        kunde.Email,
        kunde.Phone,
        Address = kunde.Address != null ? new
        {
            kunde.Address.StreetName,
            kunde.Address.BuildingNumber,
            kunde.Address.Floor,
            kunde.Address.Suite,
            kunde.Address.PostCode,
            kunde.Address.CityName
        } : null,
        IsPrivate = kunde.Cpr != null,
        IsCompany = kunde.Cvr != null,
        kunde.CreatedAt,
        Leverancer = kunde.Leverancer.Select(l => new
        {
            l.Id,
            l.MålepunktId,
            Gsrn = l.Målepunkt.Gsrn.Value,
            SupplyStart = l.SupplyPeriod.Start,
            SupplyEnd = l.SupplyPeriod.End,
            l.IsActive
        })
    });
}).WithName("GetKunde");

app.MapPost("/api/kunder", async (CreateKundeRequest req, WattsOnDbContext db) =>
{
    Address? address = req.Address != null
        ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber, req.Address.PostCode, req.Address.CityName,
            req.Address.Floor, req.Address.Suite)
        : null;

    Kunde kunde;
    if (req.Cpr != null)
    {
        kunde = Kunde.CreatePerson(req.Name, CprNumber.Create(req.Cpr), address);
    }
    else if (req.Cvr != null)
    {
        kunde = Kunde.CreateCompany(req.Name, CvrNumber.Create(req.Cvr), address);
    }
    else
    {
        return Results.BadRequest(new { error = "Either CPR or CVR is required" });
    }

    if (req.Email != null || req.Phone != null)
        kunde.UpdateContactInfo(req.Email, req.Phone);

    db.Kunder.Add(kunde);
    await db.SaveChangesAsync();

    return Results.Created($"/api/kunder/{kunde.Id}", new { kunde.Id, kunde.Name });
}).WithName("CreateKunde");

// ==================== MÅLEPUNKTER ====================

app.MapGet("/api/målepunkter", async (WattsOnDbContext db) =>
{
    var mp = await db.Målepunkter
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
}).WithName("GetMålepunkter");

app.MapGet("/api/målepunkter/{id:guid}", async (Guid id, WattsOnDbContext db) =>
{
    var mp = await db.Målepunkter
        .Include(m => m.Leverancer)
            .ThenInclude(l => l.Kunde)
        .Include(m => m.Tidsserier)
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
        Leverancer = mp.Leverancer.Select(l => new
        {
            l.Id,
            l.KundeId,
            KundeNavn = l.Kunde.Name,
            SupplyStart = l.SupplyPeriod.Start,
            SupplyEnd = l.SupplyPeriod.End,
            l.IsActive
        }),
        Tidsserier = mp.Tidsserier.OrderByDescending(t => t.ReceivedAt).Select(t => new
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
}).WithName("GetMålepunkt");

app.MapPost("/api/målepunkter", async (CreateMålepunktRequest req, WattsOnDbContext db) =>
{
    var gsrn = Gsrn.Create(req.Gsrn);
    var gridCompanyGln = GlnNumber.Create(req.GridCompanyGln);
    var type = Enum.Parse<MålepunktsType>(req.Type);
    var art = Enum.Parse<MålepunktsArt>(req.Art);
    var settlement = Enum.Parse<SettlementMethod>(req.SettlementMethod);
    var resolution = Enum.Parse<Resolution>(req.Resolution);

    Address? address = req.Address != null
        ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber, req.Address.PostCode, req.Address.CityName,
            req.Address.Floor, req.Address.Suite)
        : null;

    var mp = Målepunkt.Create(gsrn, type, art, settlement, resolution, req.GridArea, gridCompanyGln, address);

    db.Målepunkter.Add(mp);
    await db.SaveChangesAsync();

    return Results.Created($"/api/målepunkter/{mp.Id}", new { mp.Id, Gsrn = mp.Gsrn.Value });
}).WithName("CreateMålepunkt");

// ==================== LEVERANCER ====================

app.MapGet("/api/leverancer", async (WattsOnDbContext db) =>
{
    var leverancer = await db.Leverancer
        .Include(l => l.Kunde)
        .Include(l => l.Målepunkt)
        .AsNoTracking()
        .OrderByDescending(l => l.CreatedAt)
        .Select(l => new
        {
            l.Id,
            l.MålepunktId,
            Gsrn = l.Målepunkt.Gsrn.Value,
            l.KundeId,
            KundeNavn = l.Kunde.Name,
            SupplyStart = l.SupplyPeriod.Start,
            SupplyEnd = l.SupplyPeriod.End,
            l.IsActive,
            l.CreatedAt
        })
        .ToListAsync();
    return Results.Ok(leverancer);
}).WithName("GetLeverancer");

app.MapPost("/api/leverancer", async (CreateLeveranceRequest req, WattsOnDbContext db) =>
{
    // Verify references exist
    var mp = await db.Målepunkter.FindAsync(req.MålepunktId);
    if (mp is null) return Results.BadRequest(new { error = "Målepunkt not found" });

    var kunde = await db.Kunder.FindAsync(req.KundeId);
    if (kunde is null) return Results.BadRequest(new { error = "Kunde not found" });

    var aktør = await db.Aktører.FindAsync(req.AktørId);
    if (aktør is null) return Results.BadRequest(new { error = "Aktør not found" });

    var supplyPeriod = req.SupplyEnd.HasValue
        ? Period.Create(req.SupplyStart, req.SupplyEnd.Value)
        : Period.From(req.SupplyStart);

    var leverance = Leverance.Create(req.MålepunktId, req.KundeId, req.AktørId, supplyPeriod);

    mp.SetActiveSupply(true);

    db.Leverancer.Add(leverance);
    await db.SaveChangesAsync();

    return Results.Created($"/api/leverancer/{leverance.Id}", new { leverance.Id });
}).WithName("CreateLeverance");

// ==================== PROCESSER ====================

app.MapGet("/api/processer", async (WattsOnDbContext db) =>
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
            MålepunktGsrn = p.MålepunktGsrn != null ? p.MålepunktGsrn.Value : null,
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
        MålepunktGsrn = process.MålepunktGsrn?.Value,
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

app.MapGet("/api/afregninger", async (WattsOnDbContext db) =>
{
    var afregninger = await db.Afregninger
        .AsNoTracking()
        .OrderByDescending(a => a.CalculatedAt)
        .Take(100)
        .Select(a => new
        {
            a.Id,
            a.MålepunktId,
            a.LeveranceId,
            PeriodStart = a.SettlementPeriod.Start,
            PeriodEnd = a.SettlementPeriod.End,
            TotalEnergyKwh = a.TotalEnergy.Value,
            TotalAmount = a.TotalAmount.Amount,
            Currency = a.TotalAmount.Currency,
            Status = a.Status.ToString(),
            a.IsCorrection,
            a.PreviousAfregningId,
            a.ExternalInvoiceReference,
            a.InvoicedAt,
            a.CalculatedAt
        })
        .ToListAsync();
    return Results.Ok(afregninger);
}).WithName("GetAfregninger");

/// <summary>
/// Uninvoiced settlements — for external invoicing system to pick up.
/// Returns settlements with status = Beregnet that are NOT corrections.
/// </summary>
app.MapGet("/api/afregninger/uninvoiced", async (WattsOnDbContext db) =>
{
    var uninvoiced = await db.Afregninger
        .Include(a => a.Målepunkt)
        .Include(a => a.Leverance)
            .ThenInclude(l => l.Kunde)
        .Where(a => a.Status == AfregningStatus.Beregnet && !a.IsCorrection)
        .AsNoTracking()
        .OrderBy(a => a.CalculatedAt)
        .Select(a => new
        {
            a.Id,
            Gsrn = a.Målepunkt.Gsrn.Value,
            KundeId = a.Leverance.KundeId,
            KundeNavn = a.Leverance.Kunde.Name,
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
app.MapGet("/api/afregninger/adjustments", async (WattsOnDbContext db) =>
{
    var adjustments = await db.Afregninger
        .Include(a => a.Målepunkt)
        .Include(a => a.Leverance)
            .ThenInclude(l => l.Kunde)
        .Where(a => a.Status == AfregningStatus.Beregnet && a.IsCorrection)
        .AsNoTracking()
        .OrderBy(a => a.CalculatedAt)
        .Select(a => new
        {
            a.Id,
            a.PreviousAfregningId,
            Gsrn = a.Målepunkt.Gsrn.Value,
            KundeId = a.Leverance.KundeId,
            KundeNavn = a.Leverance.Kunde.Name,
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
/// External invoicing system confirms a settlement has been invoiced.
/// </summary>
app.MapPost("/api/afregninger/{id:guid}/mark-invoiced", async (Guid id, MarkInvoicedRequest req, WattsOnDbContext db) =>
{
    var afregning = await db.Afregninger.FindAsync(id);
    if (afregning is null) return Results.NotFound();

    try
    {
        afregning.MarkInvoiced(req.ExternalInvoiceReference);
        await db.SaveChangesAsync();
        return Results.Ok(new
        {
            afregning.Id,
            Status = afregning.Status.ToString(),
            afregning.ExternalInvoiceReference,
            afregning.InvoicedAt
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
}).WithName("MarkSettlementInvoiced");

// ==================== PRISER (Prices) ====================

app.MapGet("/api/priser", async (WattsOnDbContext db) =>
{
    var priser = await db.Priser
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
            PriceResolution = p.PriceResolution != null ? p.PriceResolution.ToString() : null,
            PricePointCount = p.PricePoints.Count
        })
        .ToListAsync();
    return Results.Ok(priser);
}).WithName("GetPriser");

app.MapPost("/api/priser", async (CreatePrisRequest req, WattsOnDbContext db) =>
{
    var ownerGln = GlnNumber.Create(req.OwnerGln);
    var type = Enum.Parse<PriceType>(req.Type);
    var validityPeriod = req.ValidTo.HasValue
        ? Period.Create(req.ValidFrom, req.ValidTo.Value)
        : Period.From(req.ValidFrom);
    var resolution = req.PriceResolution != null ? Enum.Parse<Resolution>(req.PriceResolution) : (Resolution?)null;

    var pris = Pris.Create(req.ChargeId, ownerGln, type, req.Description, validityPeriod, req.VatExempt, resolution);

    if (req.PricePoints != null)
    {
        foreach (var pp in req.PricePoints)
        {
            pris.AddPricePoint(pp.Timestamp, pp.Price);
        }
    }

    db.Priser.Add(pris);
    await db.SaveChangesAsync();

    return Results.Created($"/api/priser/{pris.Id}", new
    {
        pris.Id,
        pris.ChargeId,
        Type = pris.Type.ToString(),
        pris.Description,
        PricePointCount = pris.PricePoints.Count
    });
}).WithName("CreatePris");

// ==================== PRISTILKNYTNINGER (Price Links) ====================

app.MapGet("/api/pristilknytninger", async (Guid? målepunktId, WattsOnDbContext db) =>
{
    var query = db.Pristilknytninger
        .Include(pt => pt.Pris)
        .AsNoTracking();

    if (målepunktId.HasValue)
        query = query.Where(pt => pt.MålepunktId == målepunktId.Value);

    var links = await query
        .Select(pt => new
        {
            pt.Id,
            pt.MålepunktId,
            pt.PrisId,
            ChargeId = pt.Pris.ChargeId,
            PrisDescription = pt.Pris.Description,
            PrisType = pt.Pris.Type.ToString(),
            LinkFrom = pt.LinkPeriod.Start,
            LinkTo = pt.LinkPeriod.End
        })
        .ToListAsync();
    return Results.Ok(links);
}).WithName("GetPristilknytninger");

app.MapPost("/api/pristilknytninger", async (CreatePristilknytningRequest req, WattsOnDbContext db) =>
{
    var mp = await db.Målepunkter.FindAsync(req.MålepunktId);
    if (mp is null) return Results.BadRequest(new { error = "Målepunkt not found" });

    var pris = await db.Priser.FindAsync(req.PrisId);
    if (pris is null) return Results.BadRequest(new { error = "Pris not found" });

    var linkPeriod = req.LinkTo.HasValue
        ? Period.Create(req.LinkFrom, req.LinkTo.Value)
        : Period.From(req.LinkFrom);

    var link = Pristilknytning.Create(req.MålepunktId, req.PrisId, linkPeriod);

    db.Pristilknytninger.Add(link);
    await db.SaveChangesAsync();

    return Results.Created($"/api/pristilknytninger/{link.Id}", new { link.Id });
}).WithName("CreatePristilknytning");

// ==================== TIDSSERIER (Time Series) ====================

/// <summary>
/// Ingest a time series with observations for a metering point.
/// If an existing time series covers the same period, it becomes a new version
/// (the old one is marked as superseded), which triggers correction detection.
/// </summary>
app.MapPost("/api/tidsserier", async (CreateTidsserieRequest req, WattsOnDbContext db) =>
{
    // Validate metering point exists
    var mp = await db.Målepunkter.FindAsync(req.MålepunktId);
    if (mp is null) return Results.BadRequest(new { error = "Målepunkt not found" });

    var period = Period.Create(req.PeriodStart, req.PeriodEnd);
    var resolution = Enum.Parse<Resolution>(req.Resolution);

    // Check for existing time series for the same period → new version
    var existing = await db.Tidsserier
        .Where(t => t.MålepunktId == req.MålepunktId)
        .Where(t => t.Period.Start == req.PeriodStart && t.Period.End == req.PeriodEnd)
        .Where(t => t.IsLatest)
        .FirstOrDefaultAsync();

    var version = 1;
    if (existing is not null)
    {
        existing.Supersede();
        version = existing.Version + 1;
    }

    var tidsserie = Tidsserie.Create(req.MålepunktId, period, resolution, version, req.TransactionId);

    foreach (var obs in req.Observations)
    {
        var quality = Enum.Parse<QuantityQuality>(obs.Quality ?? "Målt");
        tidsserie.AddObservation(obs.Timestamp, EnergyQuantity.Create(obs.KWh), quality);
    }

    db.Tidsserier.Add(tidsserie);
    await db.SaveChangesAsync();

    return Results.Created($"/api/tidsserier/{tidsserie.Id}", new
    {
        tidsserie.Id,
        tidsserie.MålepunktId,
        PeriodStart = tidsserie.Period.Start,
        PeriodEnd = tidsserie.Period.End,
        Resolution = tidsserie.Resolution.ToString(),
        tidsserie.Version,
        tidsserie.IsLatest,
        ObservationCount = tidsserie.Observations.Count,
        TotalEnergyKwh = tidsserie.TotalEnergy.Value
    });
}).WithName("CreateTidsserie");

app.MapGet("/api/tidsserier/{id:guid}", async (Guid id, WattsOnDbContext db) =>
{
    var ts = await db.Tidsserier
        .Include(t => t.Observations.OrderBy(o => o.Timestamp))
        .AsNoTracking()
        .FirstOrDefaultAsync(t => t.Id == id);

    if (ts is null) return Results.NotFound();

    return Results.Ok(new
    {
        ts.Id,
        ts.MålepunktId,
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
}).WithName("GetTidsserie");

// ==================== SETTLEMENT DOCUMENTS (Peppol-aligned pre-invoice) ====================

/// <summary>
/// Settlement documents ready for external invoicing system.
/// Returns Peppol BIS-aligned pre-invoice documents with VAT, buyer/seller, and line items.
/// Query by status: ready (new settlements), all, or corrections.
/// </summary>
app.MapGet("/api/settlement-documents", async (string? status, WattsOnDbContext db) =>
{
    // Get our own company info (seller)
    var seller = await db.Aktører.AsNoTracking().FirstOrDefaultAsync(a => a.IsOwn);
    if (seller is null) return Results.Problem("No own aktør configured");

    var query = db.Afregninger
        .Include(a => a.Lines)
        .Include(a => a.Målepunkt)
        .Include(a => a.Leverance)
            .ThenInclude(l => l.Kunde)
        .AsNoTracking();

    // Filter: "ready" = Beregnet (default), "all" = everything
    if (status == "all")
    {
        // No filter
    }
    else if (status == "corrections")
    {
        query = query.Where(a => a.IsCorrection && a.Status == AfregningStatus.Beregnet);
    }
    else // "ready" (default)
    {
        query = query.Where(a => a.Status == AfregningStatus.Beregnet);
    }

    var settlements = await query.OrderBy(a => a.DocumentNumber).ToListAsync();

    // Load price VAT info for all referenced prices
    var prisIds = settlements.SelectMany(a => a.Lines).Select(l => l.PrisId).Distinct().ToList();
    var prisVatMap = await db.Priser
        .Where(p => prisIds.Contains(p.Id))
        .AsNoTracking()
        .ToDictionaryAsync(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

    const decimal DanishVatRate = 25.0m;

    var documents = settlements.Select(a =>
    {
        var kunde = a.Leverance.Kunde;
        var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
        var isDebit = a.IsCorrection && a.TotalAmount.Amount >= 0;

        var documentType = a.IsCorrection
            ? (isCredit ? "creditNote" : "debitNote")
            : "settlement";

        var year = a.CalculatedAt.Year;
        var documentId = $"WO-{year}-{a.DocumentNumber:D5}";
        string? originalDocumentId = null;
        if (a.PreviousAfregningId.HasValue)
        {
            var original = settlements.FirstOrDefault(s => s.Id == a.PreviousAfregningId);
            if (original is not null)
                originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
        }

        var lines = a.Lines.Select((line, idx) =>
        {
            var prisInfo = prisVatMap.GetValueOrDefault(line.PrisId);
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
                name = seller.Name,
                identifier = seller.Cvr?.Value,
                identifierScheme = "DK:CVR",
                glnNumber = seller.Gln.Value
            },
            buyer = new
            {
                name = kunde.Name,
                identifier = kunde.IsPrivate ? kunde.Cpr?.Value : kunde.Cvr?.Value,
                identifierScheme = kunde.IsPrivate ? "DK:CPR" : "DK:CVR",
                address = kunde.Address != null ? new
                {
                    streetName = kunde.Address.StreetName,
                    buildingNumber = kunde.Address.BuildingNumber,
                    floor = kunde.Address.Floor,
                    suite = kunde.Address.Suite,
                    postCode = kunde.Address.PostCode,
                    cityName = kunde.Address.CityName
                } : (object?)null
            },

            meteringPoint = new
            {
                gsrn = a.Målepunkt.Gsrn.Value,
                gridArea = a.Målepunkt.GridArea
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
    var seller = await db.Aktører.AsNoTracking().FirstOrDefaultAsync(a => a.IsOwn);
    if (seller is null) return Results.Problem("No own aktør configured");

    var a = await db.Afregninger
        .Include(a => a.Lines)
        .Include(a => a.Målepunkt)
        .Include(a => a.Leverance)
            .ThenInclude(l => l.Kunde)
        .AsNoTracking()
        .FirstOrDefaultAsync(a => a.Id == id);

    if (a is null) return Results.NotFound();

    var prisIds = a.Lines.Select(l => l.PrisId).Distinct().ToList();
    var prisVatMap = await db.Priser
        .Where(p => prisIds.Contains(p.Id))
        .AsNoTracking()
        .ToDictionaryAsync(p => p.Id, p => new { p.VatExempt, p.Description, p.ChargeId, OwnerGln = p.OwnerGln.Value });

    const decimal DanishVatRate = 25.0m;

    var kunde = a.Leverance.Kunde;
    var isCredit = a.IsCorrection && a.TotalAmount.Amount < 0;
    var documentType = a.IsCorrection
        ? (isCredit ? "creditNote" : "debitNote")
        : "settlement";

    var year = a.CalculatedAt.Year;
    var documentId = $"WO-{year}-{a.DocumentNumber:D5}";

    string? originalDocumentId = null;
    if (a.PreviousAfregningId.HasValue)
    {
        var original = await db.Afregninger.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == a.PreviousAfregningId);
        if (original is not null)
            originalDocumentId = $"WO-{original.CalculatedAt.Year}-{original.DocumentNumber:D5}";
    }

    var lines = a.Lines.Select((line, idx) =>
    {
        var prisInfo = prisVatMap.GetValueOrDefault(line.PrisId);
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
            name = seller.Name,
            identifier = seller.Cvr?.Value,
            identifierScheme = "DK:CVR",
            glnNumber = seller.Gln.Value
        },
        buyer = new
        {
            name = kunde.Name,
            identifier = kunde.IsPrivate ? kunde.Cpr?.Value : kunde.Cvr?.Value,
            identifierScheme = kunde.IsPrivate ? "DK:CPR" : "DK:CVR",
            address = kunde.Address != null ? new
            {
                streetName = kunde.Address.StreetName,
                buildingNumber = kunde.Address.BuildingNumber,
                floor = kunde.Address.Floor,
                suite = kunde.Address.Suite,
                postCode = kunde.Address.PostCode,
                cityName = kunde.Address.CityName
            } : (object?)null
        },

        meteringPoint = new
        {
            gsrn = a.Målepunkt.Gsrn.Value,
            gridArea = a.Målepunkt.GridArea
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
    var afregning = await db.Afregninger.FindAsync(id);
    if (afregning is null) return Results.NotFound();

    try
    {
        afregning.MarkInvoiced(req.ExternalInvoiceReference);
        await db.SaveChangesAsync();

        var year = afregning.CalculatedAt.Year;
        return Results.Ok(new
        {
            documentId = $"WO-{year}-{afregning.DocumentNumber:D5}",
            settlementId = afregning.Id,
            status = afregning.Status.ToString(),
            externalInvoiceReference = afregning.ExternalInvoiceReference,
            invoicedAt = afregning.InvoicedAt
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
    var ownAktør = await db.Aktører.FirstOrDefaultAsync(a => a.IsOwn);
    if (ownAktør is null) return Results.Problem("No own aktør configured");

    // 1. Find or create the metering point
    var gsrn = Gsrn.Create(req.Gsrn);
    var mp = await db.Målepunkter.FirstOrDefaultAsync(m => m.Gsrn.Value == req.Gsrn);
    if (mp is null)
    {
        var gridGln = GlnNumber.Create(req.GridCompanyGln ?? "5790000610976");
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        mp = Målepunkt.Create(gsrn, MålepunktsType.Forbrug, MålepunktsArt.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, req.GridArea ?? "DK1", gridGln, address);
        db.Målepunkter.Add(mp);
    }

    // 2. Find or create the customer
    Kunde? kunde = null;
    if (req.CprNumber != null)
        kunde = await db.Kunder.FirstOrDefaultAsync(k => k.Cpr != null && k.Cpr.Value == req.CprNumber);
    else if (req.CvrNumber != null)
        kunde = await db.Kunder.FirstOrDefaultAsync(k => k.Cvr != null && k.Cvr.Value == req.CvrNumber);

    if (kunde is null)
    {
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        kunde = req.CprNumber != null
            ? Kunde.CreatePerson(req.CustomerName, CprNumber.Create(req.CprNumber), address)
            : Kunde.CreateCompany(req.CustomerName, CvrNumber.Create(req.CvrNumber!), address);
        if (req.Email != null || req.Phone != null)
            kunde.UpdateContactInfo(req.Email, req.Phone);
        db.Kunder.Add(kunde);
    }

    // 3. Check for existing leverance (if another supplier had this customer)
    var currentLeverance = await db.Leverancer
        .Where(l => l.MålepunktId == mp.Id)
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
        process, mp, kunde, ownAktør.Id, currentLeverance);

    mp.SetActiveSupply(true);

    // 7. Create simulated inbox messages (audit trail)
    var requestMsg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-001", "5790000432752", ownAktør.Gln.Value,
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
        "RSM-001", "5790000432752", ownAktør.Gln.Value,
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
    var existingLinks = await db.Pristilknytninger
        .Where(pt => pt.MålepunktId == mp.Id)
        .CountAsync();

    var newPriceLinks = 0;
    if (existingLinks == 0)
    {
        var allPrices = await db.Priser.ToListAsync();
        foreach (var pris in allPrices)
        {
            var link = Pristilknytning.Create(mp.Id, pris.Id, Period.From(req.EffectiveDate));
            db.Pristilknytninger.Add(link);
            newPriceLinks++;
        }
    }

    // 9. Generate simulated time series with consumption data
    Tidsserie? tidsserie = null;
    if (req.GenerateConsumption)
    {
        var periodStart = req.EffectiveDate;
        // Generate one month of data from effective date
        var periodEnd = new DateTimeOffset(
            periodStart.Year, periodStart.Month, 1, 0, 0, 0, periodStart.Offset)
            .AddMonths(1);

        tidsserie = Tidsserie.Create(mp.Id, Period.Create(periodStart, periodEnd),
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
            tidsserie.AddObservation(ts, EnergyQuantity.Create(baseKwh), QuantityQuality.Målt);
        }
        db.Tidsserier.Add(tidsserie);
    }

    // Save everything
    db.Processes.Add(process);
    if (result.NewLeverance != null) db.Leverancer.Add(result.NewLeverance);
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
        customerName = kunde.Name,
        customerId = kunde.Id,
        newLeveranceId = result.NewLeverance?.Id,
        endedLeveranceId = result.EndedLeverance?.Id,
        effectiveDate = req.EffectiveDate,
        priceLinksCreated = newPriceLinks,
        timeSeriesGenerated = tidsserie != null,
        totalEnergyKwh = tidsserie?.TotalEnergy.Value,
        message = $"Leverandørskift gennemført for {kunde.Name} på {req.Gsrn}. " +
                  $"Effektiv dato: {req.EffectiveDate:yyyy-MM-dd}." +
                  (tidsserie != null ? $" Genereret {tidsserie.Observations.Count} timer forbrugsdata ({tidsserie.TotalEnergy.Value:F1} kWh)." : "") +
                  (newPriceLinks > 0 ? $" {newPriceLinks} priser tilknyttet." : "") +
                  " SettlementWorker beregner automatisk afregning inden for 30 sekunder."
    });
}).WithName("SimulateSupplierChange");

/// <summary>
/// BRS-001 Recipient: Lose a customer — another supplier takes over.
/// Ends the leverance and triggers final settlement.
/// </summary>
app.MapPost("/api/simulation/supplier-change-outgoing", async (SimulateOutgoingSupplierChangeRequest req, WattsOnDbContext db) =>
{
    // Find the leverance
    var leverance = await db.Leverancer
        .Include(l => l.Kunde)
        .Include(l => l.Målepunkt)
        .Where(l => l.Id == req.LeveranceId)
        .FirstOrDefaultAsync();

    if (leverance is null)
        return Results.NotFound("Leverance ikke fundet");

    if (!leverance.IsActive)
        return Results.Problem("Leverance er allerede afsluttet");

    var gsrn = leverance.Målepunkt.Gsrn;
    var newSupplierGln = GlnNumber.Create(req.NewSupplierGln ?? "5790000000005");

    var result = Brs001Handler.HandleAsRecipient(
        gsrn, req.EffectiveDate,
        $"DH-SIM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
        newSupplierGln, leverance);

    leverance.Målepunkt.SetActiveSupply(false);

    // Create audit trail inbox message
    var ownAktør = await db.Aktører.FirstOrDefaultAsync(a => a.IsOwn);
    var msg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-004", "5790000432752", ownAktør?.Gln.Value ?? "unknown",
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
        customerName = leverance.Kunde.Name,
        customerId = leverance.Kunde.Id,
        endedLeveranceId = result.EndedLeverance?.Id,
        effectiveDate = req.EffectiveDate,
        message = $"Leverandørskift udgående — {leverance.Kunde.Name} forlader os på {gsrn.Value}. " +
                  $"Leverance afsluttet pr. {req.EffectiveDate:yyyy-MM-dd}. Slutafregning beregnes automatisk."
    });
}).WithName("SimulateOutgoingSupplierChange");

/// <summary>
/// BRS-009: Move-in — new customer at a metering point.
/// </summary>
app.MapPost("/api/simulation/move-in", async (SimulateMoveInRequest req, WattsOnDbContext db) =>
{
    var ownAktør = await db.Aktører.FirstOrDefaultAsync(a => a.IsOwn);
    if (ownAktør is null) return Results.Problem("No own aktør configured");

    // Find or create metering point
    var gsrn = Gsrn.Create(req.Gsrn);
    var mp = await db.Målepunkter.FirstOrDefaultAsync(m => m.Gsrn.Value == req.Gsrn);
    if (mp is null)
    {
        var gridGln = GlnNumber.Create(req.GridCompanyGln ?? "5790000610976");
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        mp = Målepunkt.Create(gsrn, MålepunktsType.Forbrug, MålepunktsArt.Fysisk,
            SettlementMethod.Flex, Resolution.PT1H, req.GridArea ?? "DK1", gridGln, address);
        db.Målepunkter.Add(mp);
    }

    // Find or create customer
    Kunde? kunde = null;
    if (req.CprNumber != null)
        kunde = await db.Kunder.FirstOrDefaultAsync(k => k.Cpr != null && k.Cpr.Value == req.CprNumber);
    else if (req.CvrNumber != null)
        kunde = await db.Kunder.FirstOrDefaultAsync(k => k.Cvr != null && k.Cvr.Value == req.CvrNumber);

    if (kunde is null)
    {
        var address = req.Address != null
            ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
            : null;
        kunde = req.CprNumber != null
            ? Kunde.CreatePerson(req.CustomerName, CprNumber.Create(req.CprNumber), address)
            : Kunde.CreateCompany(req.CustomerName, CvrNumber.Create(req.CvrNumber!), address);
        if (req.Email != null || req.Phone != null)
            kunde.UpdateContactInfo(req.Email, req.Phone);
        db.Kunder.Add(kunde);
    }

    // Check for existing leverance (previous tenant)
    var currentLeverance = await db.Leverancer
        .Where(l => l.MålepunktId == mp.Id)
        .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > req.EffectiveDate)
        .FirstOrDefaultAsync();

    var result = Brs009Handler.ExecuteMoveIn(
        gsrn, req.EffectiveDate, req.CprNumber, req.CvrNumber,
        mp, kunde, ownAktør.Id, currentLeverance);

    mp.SetActiveSupply(true);

    // Link prices
    var existingLinks = await db.Pristilknytninger
        .Where(pt => pt.MålepunktId == mp.Id).CountAsync();
    var newPriceLinks = 0;
    if (existingLinks == 0)
    {
        foreach (var pris in await db.Priser.ToListAsync())
        {
            db.Pristilknytninger.Add(Pristilknytning.Create(mp.Id, pris.Id, Period.From(req.EffectiveDate)));
            newPriceLinks++;
        }
    }

    // Generate consumption data
    Tidsserie? tidsserie = null;
    if (req.GenerateConsumption)
    {
        var periodStart = req.EffectiveDate;
        var periodEnd = new DateTimeOffset(
            periodStart.Year, periodStart.Month, 1, 0, 0, 0, periodStart.Offset).AddMonths(1);

        tidsserie = Tidsserie.Create(mp.Id, Period.Create(periodStart, periodEnd),
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
            tidsserie.AddObservation(ts, EnergyQuantity.Create(baseKwh), QuantityQuality.Målt);
        }
        db.Tidsserier.Add(tidsserie);
    }

    // Audit trail
    var inboxMsg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-001", "5790000432752", ownAktør.Gln.Value,
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
    db.Leverancer.Add(result.NewLeverance);
    db.InboxMessages.Add(inboxMsg);
    await db.SaveChangesAsync();

    return Results.Created($"/api/processer/{result.Process.Id}", new
    {
        processId = result.Process.Id,
        transactionId = result.Process.TransactionId,
        status = result.Process.Status.ToString(),
        currentState = result.Process.CurrentState,
        gsrn = req.Gsrn,
        customerName = kunde.Name,
        customerId = kunde.Id,
        newLeveranceId = result.NewLeverance.Id,
        endedLeveranceId = result.EndedLeverance?.Id,
        previousCustomerName = result.EndedLeverance != null ? "Tidligere lejer" : null,
        effectiveDate = req.EffectiveDate,
        priceLinksCreated = newPriceLinks,
        timeSeriesGenerated = tidsserie != null,
        totalEnergyKwh = tidsserie?.TotalEnergy.Value,
        message = $"Tilflytning gennemført for {kunde.Name} på {req.Gsrn}. " +
                  $"Effektiv dato: {req.EffectiveDate:yyyy-MM-dd}." +
                  (result.EndedLeverance != null ? " Tidligere leverance afsluttet." : "") +
                  (tidsserie != null ? $" Genereret {tidsserie.Observations.Count} timer forbrugsdata ({tidsserie.TotalEnergy.Value:F1} kWh)." : "") +
                  (newPriceLinks > 0 ? $" {newPriceLinks} priser tilknyttet." : "") +
                  " SettlementWorker beregner automatisk afregning inden for 30 sekunder."
    });
}).WithName("SimulateMoveIn");

/// <summary>
/// Move-out: Customer leaves a metering point. Ends leverance, final settlement.
/// </summary>
app.MapPost("/api/simulation/move-out", async (SimulateMoveOutRequest req, WattsOnDbContext db) =>
{
    var leverance = await db.Leverancer
        .Include(l => l.Kunde)
        .Include(l => l.Målepunkt)
        .Where(l => l.Id == req.LeveranceId)
        .FirstOrDefaultAsync();

    if (leverance is null)
        return Results.NotFound("Leverance ikke fundet");

    if (!leverance.IsActive)
        return Results.Problem("Leverance er allerede afsluttet");

    var gsrn = leverance.Målepunkt.Gsrn;
    var result = Brs009Handler.ExecuteMoveOut(gsrn, req.EffectiveDate, leverance);

    leverance.Målepunkt.SetActiveSupply(false);

    // Audit trail
    var ownAktør = await db.Aktører.FirstOrDefaultAsync(a => a.IsOwn);
    var msg = InboxMessage.Create(
        $"MSG-{Guid.NewGuid().ToString()[..8]}",
        "RSM-001", "5790000432752", ownAktør?.Gln.Value ?? "unknown",
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
        customerName = leverance.Kunde.Name,
        customerId = leverance.Kunde.Id,
        endedLeveranceId = result.EndedLeverance.Id,
        effectiveDate = req.EffectiveDate,
        message = $"Fraflytning gennemført — {leverance.Kunde.Name} fraflyttet {gsrn.Value}. " +
                  $"Leverance afsluttet pr. {req.EffectiveDate:yyyy-MM-dd}. Slutafregning beregnes automatisk."
    });
}).WithName("SimulateMoveOut");

// ==================== DASHBOARD ====================

app.MapGet("/api/dashboard", async (WattsOnDbContext db) =>
{
    var kundeCount = await db.Kunder.CountAsync();
    var mpCount = await db.Målepunkter.CountAsync();
    var activeLeverancer = await db.Leverancer.CountAsync(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > DateTimeOffset.UtcNow);
    var activeProcesses = await db.Processes.CountAsync(p => p.CompletedAt == null);
    var unprocessedInbox = await db.InboxMessages.CountAsync(m => !m.IsProcessed);
    var unsentOutbox = await db.OutboxMessages.CountAsync(m => !m.IsSent);

    // Settlement stats
    var beregnede = await db.Afregninger.CountAsync(a => a.Status == AfregningStatus.Beregnet && !a.IsCorrection);
    var fakturerede = await db.Afregninger.CountAsync(a => a.Status == AfregningStatus.Faktureret);
    var justerede = await db.Afregninger.CountAsync(a => a.Status == AfregningStatus.Justeret);
    var korrektioner = await db.Afregninger.CountAsync(a => a.IsCorrection && a.Status == AfregningStatus.Beregnet);
    var totalSettlementAmount = await db.Afregninger
        .Where(a => !a.IsCorrection)
        .SumAsync(a => a.TotalAmount.Amount);

    return Results.Ok(new
    {
        kunder = kundeCount,
        målepunkter = mpCount,
        aktiveLeverancer = activeLeverancer,
        aktiveProcesser = activeProcesses,
        ubehandledeInbox = unprocessedInbox,
        uafsendeOutbox = unsentOutbox,
        afregninger = new
        {
            beregnede,
            fakturerede,
            justerede,
            korrektioner,
            totalBeløb = totalSettlementAmount,
        }
    });
}).WithName("GetDashboard");

app.Run();

// ==================== REQUEST DTOs ====================

record CreateAktørRequest(string Gln, string Name, string Role, string? Cvr, bool IsOwn = false);

record AddressDto(string StreetName, string BuildingNumber, string PostCode, string CityName,
    string? Floor = null, string? Suite = null);

record CreateKundeRequest(string Name, string? Cpr, string? Cvr, string? Email, string? Phone, AddressDto? Address);

record CreateMålepunktRequest(string Gsrn, string Type, string Art, string SettlementMethod,
    string Resolution, string GridArea, string GridCompanyGln, AddressDto? Address);

record CreateLeveranceRequest(Guid MålepunktId, Guid KundeId, Guid AktørId,
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

record CreatePristilknytningRequest(
    Guid MålepunktId,
    Guid PrisId,
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
    Guid LeveranceId,
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
    Guid LeveranceId,
    DateTimeOffset EffectiveDate);

record CreateTidsserieRequest(
    Guid MålepunktId,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    string Resolution,
    string? TransactionId,
    List<ObservationDto> Observations);

record ObservationDto(DateTimeOffset Timestamp, decimal KWh, string? Quality);
