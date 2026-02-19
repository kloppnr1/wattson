using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Polls for unprocessed inbox messages and routes them to the appropriate BRS handler.
/// Supports BRS-001 (Leverandørskift), BRS-006 (Stamdata), BRS-009 (Tilflytning/Fraflytning),
/// BRS-021 (Måledata), BRS-023 (Beregnede tidsserier), BRS-027 (Engrosydelser),
/// BRS-031 (Prisoplysninger) and BRS-037 (Pristilknytninger) messages.
/// </summary>
public class InboxPollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboxPollingWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);

    public InboxPollingWorker(IServiceScopeFactory scopeFactory, ILogger<InboxPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InboxPollingWorker starting — polling every {Interval}s", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessages(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inbox messages");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMessages(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        var pending = await db.InboxMessages
            .Where(m => !m.IsProcessed && m.ProcessingAttempts < 5)
            .OrderBy(m => m.ReceivedAt)
            .Take(10)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        _logger.LogInformation("Processing {Count} inbox messages", pending.Count);

        foreach (var message in pending)
        {
            try
            {
                await RouteMessage(db, message, ct);
                message.MarkProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process message {MessageId} ({DocType}/{BizProcess})",
                    message.MessageId, message.DocumentType, message.BusinessProcess);
                message.MarkFailed(ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task RouteMessage(WattsOnDbContext db, InboxMessage message, CancellationToken ct)
    {
        _logger.LogInformation("Routing message {MessageId}: {DocumentType}/{BusinessProcess}",
            message.MessageId, message.DocumentType, message.BusinessProcess);

        var payload = !string.IsNullOrEmpty(message.RawPayload)
            ? JsonSerializer.Deserialize<JsonElement>(message.RawPayload)
            : default;

        switch (message.BusinessProcess)
        {
            case "BRS-001":
                await HandleBrs001Message(db, message, payload, ct);
                break;

            case "BRS-006":
                await HandleBrs006Message(db, message, payload, ct);
                break;

            case "BRS-009":
                await HandleBrs009Message(db, message, payload, ct);
                break;

            case "BRS-021":
                await HandleBrs021Message(db, message, payload, ct);
                break;

            case "BRS-023":
                await HandleBrs023Message(db, message, payload, ct);
                break;

            case "BRS-027":
                await HandleBrs027Message(db, message, payload, ct);
                break;

            case "BRS-031":
            case "BRS-037": // Price link updates — same D17/D08/D18 message format as BRS-031
                await HandleBrs031Message(db, message, payload, ct);
                break;

            default:
                // Unknown business process — log and mark as processed
                _logger.LogInformation(
                    "No handler for business process '{BusinessProcess}' — marking as processed",
                    message.BusinessProcess);
                break;
        }
    }

    private async Task HandleBrs001Message(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        switch (message.DocumentType)
        {
            case "RSM-001": // Confirmation from DataHub
            {
                var gsrn = GetPayloadString(payload, "gsrn");
                var transactionId = GetPayloadString(payload, "transactionId") ?? message.MessageId;

                if (string.IsNullOrEmpty(gsrn))
                {
                    _logger.LogWarning("BRS-001 RSM-001 missing GSRN — skipping message {MessageId}", message.MessageId);
                    break;
                }

                // Find the active BRS-001 process for this GSRN
                var process = await db.Processes
                    .Where(p => p.ProcessType == ProcessType.Leverandørskift)
                    .Where(p => p.MeteringPointGsrn != null && p.MeteringPointGsrn.Value == gsrn)
                    .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
                    .OrderByDescending(p => p.StartedAt)
                    .FirstOrDefaultAsync(ct);

                if (process != null)
                {
                    Brs001Handler.HandleConfirmation(process, transactionId);
                    _logger.LogInformation("BRS-001 confirmed for GSRN {Gsrn}, transaction {TransactionId}", gsrn, transactionId);

                    // Auto-execute if we have the customer data
                    var effectiveDate = process.EffectiveDate;
                    if (effectiveDate.HasValue)
                    {
                        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == Gsrn.Create(gsrn), ct);
                        if (mp != null)
                        {
                            var supply = await db.Supplies
                                .Where(s => s.MeteringPointId == mp.Id)
                                .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > effectiveDate.Value)
                                .FirstOrDefaultAsync(ct);

                            // Find our actor
                            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive, ct);

                            // Find or get customer from process context
                            var customerId = supply?.CustomerId;
                            if (customerId.HasValue)
                            {
                                var customer = await db.Customers.FindAsync(new object[] { customerId.Value }, ct);
                                if (customer != null && identity != null)
                                {
                                    var result = Brs001Handler.ExecuteSupplierChange(
                                        process, mp, customer, supply);

                                    if (result.NewSupply != null)
                                        db.Supplies.Add(result.NewSupply);
                                }
                            }
                        }
                    }
                }
                break;
            }

            case "RSM-004": // Stop-of-supply notification (we're losing a customer)
            {
                var gsrn = GetPayloadString(payload, "gsrn");
                var effectiveDateStr = GetPayloadString(payload, "effectiveDate");
                var newSupplierGln = GetPayloadString(payload, "newSupplierGln");
                var transactionId = GetPayloadString(payload, "transactionId") ?? message.MessageId;

                if (gsrn == null || effectiveDateStr == null) break;

                var effectiveDate = DateTimeOffset.Parse(effectiveDateStr);
                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == Gsrn.Create(gsrn), ct);
                if (mp == null) break;

                var currentSupply = await db.Supplies
                    .Where(s => s.MeteringPointId == mp.Id)
                    .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > effectiveDate)
                    .FirstOrDefaultAsync(ct);

                if (currentSupply != null)
                {
                    var result = Brs001Handler.HandleAsRecipient(
                        Gsrn.Create(gsrn),
                        effectiveDate,
                        transactionId,
                        GlnNumber.Create(newSupplierGln ?? "5790000000005"),
                        currentSupply);

                    db.Processes.Add(result.Process);
                    _logger.LogInformation("BRS-001 recipient: losing supply for GSRN {Gsrn} effective {Date}",
                        gsrn, effectiveDate);
                }
                break;
            }
        }
    }

    private async Task HandleBrs009Message(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        switch (message.DocumentType)
        {
            case "RSM-001": // Move-in confirmation
            {
                var gsrn = GetPayloadString(payload, "gsrn");
                var transactionId = GetPayloadString(payload, "transactionId") ?? message.MessageId;

                _logger.LogInformation("BRS-009 move-in confirmation for GSRN {Gsrn}", gsrn);
                // Find and update the process
                var process = await db.Processes
                    .Where(p => p.ProcessType == ProcessType.Tilflytning)
                    .Where(p => p.MeteringPointGsrn != null && p.MeteringPointGsrn.Value == gsrn)
                    .Where(p => p.Status != ProcessStatus.Completed)
                    .OrderByDescending(p => p.StartedAt)
                    .FirstOrDefaultAsync(ct);

                if (process != null)
                {
                    process.MarkSubmitted(transactionId);
                    process.MarkConfirmed();
                    process.MarkCompleted();
                    _logger.LogInformation("BRS-009 process {ProcessId} completed for GSRN {Gsrn}", process.Id, gsrn);
                }
                break;
            }

            case "RSM-004": // Move-out notification
            {
                var gsrn = GetPayloadString(payload, "gsrn");
                var effectiveDateStr = GetPayloadString(payload, "effectiveDate");

                if (gsrn == null || effectiveDateStr == null) break;

                var effectiveDate = DateTimeOffset.Parse(effectiveDateStr);
                _logger.LogInformation("BRS-009 move-out for GSRN {Gsrn} effective {Date}", gsrn, effectiveDate);

                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == Gsrn.Create(gsrn), ct);
                if (mp == null) break;

                var supply = await db.Supplies
                    .Where(s => s.MeteringPointId == mp.Id)
                    .Where(s => s.SupplyPeriod.End == null)
                    .FirstOrDefaultAsync(ct);

                if (supply != null)
                {
                    var (process, _) = Brs009Handler.ExecuteMoveOut(
                        Gsrn.Create(gsrn), effectiveDate, supply);
                    db.Processes.Add(process);
                }
                break;
            }
        }
    }

    private async Task HandleBrs031Message(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var brs = message.BusinessProcess; // BRS-031 or BRS-037
        var businessReason = GetPayloadString(payload, "businessReason") ?? message.DocumentType;

        switch (businessReason)
        {
            case "D18": // Price information / charge masterdata (create/update)
            {
                var chargeId = GetPayloadString(payload, "chargeId")!;
                var ownerGln = GlnNumber.Create(GetPayloadString(payload, "ownerGln")!);
                var priceTypeStr = GetPayloadString(payload, "priceType")!;
                var description = GetPayloadString(payload, "description") ?? "";
                var effectiveDate = DateTimeOffset.Parse(GetPayloadString(payload, "effectiveDate")!);
                var stopDateStr = GetPayloadString(payload, "stopDate");
                var stopDate = stopDateStr != null ? DateTimeOffset.Parse(stopDateStr) : (DateTimeOffset?)null;
                var resolutionStr = GetPayloadString(payload, "resolution") ?? "PT1H";
                var vatExempt = payload.TryGetProperty("vatExempt", out var ve) && ve.GetBoolean();
                var isTax = payload.TryGetProperty("isTax", out var it) && it.GetBoolean();
                var isPassThrough = !payload.TryGetProperty("isPassThrough", out var ipt) || ipt.GetBoolean();

                var priceType = Enum.Parse<PriceType>(priceTypeStr, ignoreCase: true);
                var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);

                // Find existing price by ChargeId + OwnerGln
                var existingPrice = await db.Prices
                    .Include(p => p.PricePoints)
                    .Where(p => p.ChargeId == chargeId)
                    .FirstOrDefaultAsync(p => p.OwnerGln == ownerGln, ct);

                var result = Brs031Handler.ProcessPriceInformation(
                    chargeId, ownerGln, priceType, description, effectiveDate, stopDate,
                    resolution, vatExempt, isTax, isPassThrough, existingPrice);

                if (result.IsNew)
                    db.Prices.Add(result.Price);

                _logger.LogInformation("{Brs} D18: {Action} price info {ChargeId} for {OwnerGln}",
                    brs, result.IsNew ? "Created" : "Updated", chargeId, ownerGln);
                break;
            }

            case "D08": // Price series / charge prices (add/replace points)
            {
                var chargeId = GetPayloadString(payload, "chargeId")!;
                var ownerGln = GlnNumber.Create(GetPayloadString(payload, "ownerGln")!);
                var startDate = DateTimeOffset.Parse(GetPayloadString(payload, "startDate")!);
                var endDate = DateTimeOffset.Parse(GetPayloadString(payload, "endDate")!);

                var price = await db.Prices
                    .Include(p => p.PricePoints)
                    .Where(p => p.ChargeId == chargeId)
                    .FirstOrDefaultAsync(p => p.OwnerGln == ownerGln, ct);

                if (price is null)
                {
                    _logger.LogWarning("{Brs} D08: Price not found for {ChargeId}/{OwnerGln}", brs, chargeId, ownerGln);
                    break;
                }

                var points = new List<(DateTimeOffset timestamp, decimal price)>();
                if (payload.TryGetProperty("points", out var pointsArray))
                {
                    foreach (var pt in pointsArray.EnumerateArray())
                    {
                        var timestamp = DateTimeOffset.Parse(pt.GetProperty("timestamp").GetString()!);
                        var amount = pt.GetProperty("price").GetDecimal();
                        points.Add((timestamp, amount));
                    }
                }

                var result = Brs031Handler.ProcessPriceSeries(price, startDate, endDate, points);

                _logger.LogInformation("{Brs} D08: Added {Count} price points to {ChargeId}", brs, result.PointsAdded, chargeId);
                break;
            }

            case "D17": // Price link update
            {
                var gsrnStr = GetPayloadString(payload, "gsrn")!;
                var chargeId = GetPayloadString(payload, "chargeId")!;
                var ownerGln = GlnNumber.Create(GetPayloadString(payload, "ownerGln")!);
                var linkStart = DateTimeOffset.Parse(GetPayloadString(payload, "linkStart")!);
                var linkEndStr = GetPayloadString(payload, "linkEnd");
                var linkEnd = linkEndStr != null ? DateTimeOffset.Parse(linkEndStr) : (DateTimeOffset?)null;

                // Find metering point by GSRN
                var gsrn = Gsrn.Create(gsrnStr);
                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == gsrn, ct);
                if (mp is null)
                {
                    _logger.LogWarning("{Brs} D17: Metering point not found for GSRN {Gsrn}", brs, gsrnStr);
                    break;
                }

                // Find price by ChargeId + OwnerGln
                var price = await db.Prices
                    .Where(p => p.ChargeId == chargeId)
                    .FirstOrDefaultAsync(p => p.OwnerGln == ownerGln, ct);

                if (price is null)
                {
                    _logger.LogWarning("{Brs} D17: Price not found for {ChargeId}/{OwnerGln}", brs, chargeId, ownerGln);
                    break;
                }

                // Find existing link
                var existingLink = await db.PriceLinks
                    .FirstOrDefaultAsync(pl => pl.PriceId == price.Id && pl.MeteringPointId == mp.Id, ct);

                var result = Brs031Handler.ProcessPriceLinkUpdate(
                    mp.Id, price.Id, linkStart, linkEnd, existingLink);

                if (result.IsNew)
                    db.PriceLinks.Add(result.Link);

                _logger.LogInformation("{Brs} D17: {Action} price link for GSRN {Gsrn} → {ChargeId}",
                    brs, result.IsNew ? "Created" : "Updated", gsrnStr, chargeId);
                break;
            }

            default:
                _logger.LogInformation("{Brs}: Unknown business reason '{Reason}' — skipping", brs, businessReason);
                break;
        }
    }

    private async Task HandleBrs021Message(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = GetPayloadString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-021: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == gsrn, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-021: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var periodStartStr = GetPayloadString(payload, "periodStart");
        var periodEndStr = GetPayloadString(payload, "periodEnd");
        if (periodStartStr is null || periodEndStr is null)
        {
            _logger.LogWarning("BRS-021: Missing period for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var periodStart = DateTimeOffset.Parse(periodStartStr);
        var periodEnd = DateTimeOffset.Parse(periodEndStr);
        var resolutionStr = GetPayloadString(payload, "resolution") ?? "PT1H";
        var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);
        var transactionId = GetPayloadString(payload, "transactionId") ?? message.MessageId;

        // Parse observations
        var observations = new List<Brs021Handler.ObservationData>();
        if (payload.TryGetProperty("observations", out var obsArray))
        {
            foreach (var obs in obsArray.EnumerateArray())
            {
                var timestamp = DateTimeOffset.Parse(obs.GetProperty("timestamp").GetString()!);
                var kwh = obs.GetProperty("kwh").GetDecimal();
                var qualityCode = obs.TryGetProperty("quality", out var q) ? q.GetString() : null;
                var quality = Brs021Handler.MapQuantityStatus(qualityCode);
                observations.Add(new Brs021Handler.ObservationData(timestamp, kwh, quality));
            }
        }

        if (observations.Count == 0)
        {
            _logger.LogWarning("BRS-021: No observations in message for GSRN {Gsrn}", gsrnStr);
            return;
        }

        // Find existing latest time series for the same period
        var existingLatest = await db.TimeSeriesCollection
            .Where(t => t.MeteringPointId == mp.Id)
            .Where(t => t.Period.Start == periodStart && t.Period.End == periodEnd)
            .Where(t => t.IsLatest)
            .FirstOrDefaultAsync(ct);

        var result = Brs021Handler.ProcessMeteredData(
            mp.Id, periodStart, periodEnd, resolution, observations, transactionId, existingLatest);

        db.TimeSeriesCollection.Add(result.TimeSeries);

        _logger.LogInformation(
            "BRS-021: {Action} time series for GSRN {Gsrn}, period {Start}—{End}, v{Version}, {Count} observations, {TotalKwh:F3} kWh",
            result.SupersededVersion is not null ? "Updated" : "Created",
            gsrnStr,
            periodStart.ToString("yyyy-MM-dd"),
            periodEnd.ToString("yyyy-MM-dd"),
            result.TimeSeries.Version,
            observations.Count,
            result.TimeSeries.TotalEnergy.Value);
    }

    private async Task HandleBrs006Message(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = GetPayloadString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-006: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn == gsrn, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-006: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        // Parse optional update fields
        var typeStr = GetPayloadString(payload, "type");
        var artStr = GetPayloadString(payload, "art");
        var settlementStr = GetPayloadString(payload, "settlementMethod");
        var resolutionStr = GetPayloadString(payload, "resolution");
        var connectionStr = GetPayloadString(payload, "connectionState");
        var gridArea = GetPayloadString(payload, "gridArea");
        var gridCompanyGlnStr = GetPayloadString(payload, "gridCompanyGln");

        // Parse address if present
        Address? address = null;
        if (payload.TryGetProperty("address", out var addrEl) && addrEl.ValueKind == JsonValueKind.Object)
        {
            var street = addrEl.TryGetProperty("streetName", out var s) ? s.GetString() ?? "" : "";
            var building = addrEl.TryGetProperty("buildingNumber", out var b) ? b.GetString() ?? "" : "";
            var postCode = addrEl.TryGetProperty("postCode", out var pc) ? pc.GetString() ?? "" : "";
            var city = addrEl.TryGetProperty("cityName", out var ci) ? ci.GetString() ?? "" : "";
            var floor = addrEl.TryGetProperty("floor", out var fl) ? fl.GetString() : null;
            var suite = addrEl.TryGetProperty("suite", out var su) ? su.GetString() : null;
            address = Address.Create(street, building, postCode, city, floor, suite);
        }

        var update = new Brs006Handler.MasterDataUpdate(
            Type: typeStr != null ? Enum.Parse<MeteringPointType>(typeStr, ignoreCase: true) : null,
            Art: artStr != null ? Enum.Parse<MeteringPointCategory>(artStr, ignoreCase: true) : null,
            SettlementMethod: settlementStr != null ? Enum.Parse<SettlementMethod>(settlementStr, ignoreCase: true) : null,
            Resolution: resolutionStr != null ? Enum.Parse<Resolution>(resolutionStr, ignoreCase: true) : null,
            ConnectionState: connectionStr != null ? Enum.Parse<ConnectionState>(connectionStr, ignoreCase: true) : null,
            GridArea: gridArea,
            GridCompanyGln: gridCompanyGlnStr != null ? GlnNumber.Create(gridCompanyGlnStr) : null,
            Address: address);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        if (result.ChangedFields.Count > 0)
        {
            _logger.LogInformation("BRS-006: Updated GSRN {Gsrn}: {Fields}",
                gsrnStr, string.Join(", ", result.ChangedFields));
        }
        else
        {
            _logger.LogInformation("BRS-006: No changes for GSRN {Gsrn}", gsrnStr);
        }
    }

    private Task HandleBrs023Message(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gridArea = GetPayloadString(payload, "gridArea") ?? "Unknown";
        var businessReason = GetPayloadString(payload, "businessReason") ?? message.DocumentType;
        var mpType = GetPayloadString(payload, "meteringPointType") ?? "E17";
        var settlementMethod = GetPayloadString(payload, "settlementMethod");
        var periodStartStr = GetPayloadString(payload, "periodStart");
        var periodEndStr = GetPayloadString(payload, "periodEnd");
        var resolutionStr = GetPayloadString(payload, "resolution") ?? "PT1H";
        var qualityStatus = GetPayloadString(payload, "qualityStatus") ?? "Measured";
        var transactionId = GetPayloadString(payload, "transactionId") ?? message.MessageId;

        if (periodStartStr is null || periodEndStr is null)
        {
            _logger.LogWarning("BRS-023: Missing period — skipping message {MessageId}", message.MessageId);
            return Task.CompletedTask;
        }

        var periodStart = DateTimeOffset.Parse(periodStartStr);
        var periodEnd = DateTimeOffset.Parse(periodEndStr);
        var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);

        var observations = new List<Brs023Handler.AggregatedObservationData>();
        if (payload.TryGetProperty("observations", out var obsArray))
        {
            foreach (var obs in obsArray.EnumerateArray())
            {
                var timestamp = DateTimeOffset.Parse(obs.GetProperty("timestamp").GetString()!);
                var kwh = obs.GetProperty("kwh").GetDecimal();
                observations.Add(new Brs023Handler.AggregatedObservationData(timestamp, kwh));
            }
        }

        var result = Brs023Handler.ProcessAggregatedData(
            gridArea, businessReason, mpType, settlementMethod,
            periodStart, periodEnd, resolution, qualityStatus, transactionId, observations);

        db.AggregatedTimeSeriesCollection.Add(result.TimeSeries);

        var label = Brs023Handler.MapBusinessReasonToLabel(businessReason);
        _logger.LogInformation(
            "BRS-023: Stored {Label} for {GridArea} ({MpType}/{Settlement}), {Start}—{End}, {Count} obs, {Total:F3} kWh",
            label, gridArea, mpType, settlementMethod ?? "all",
            periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"),
            observations.Count, result.TimeSeries.TotalEnergyKwh);

        return Task.CompletedTask;
    }

    private Task HandleBrs027Message(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gridArea = GetPayloadString(payload, "gridArea") ?? "Unknown";
        var businessReason = GetPayloadString(payload, "businessReason") ?? "D05";
        var periodStartStr = GetPayloadString(payload, "periodStart");
        var periodEndStr = GetPayloadString(payload, "periodEnd");
        var resolutionStr = GetPayloadString(payload, "resolution") ?? "PT1H";
        var transactionId = GetPayloadString(payload, "transactionId") ?? message.MessageId;

        if (periodStartStr is null || periodEndStr is null)
        {
            _logger.LogWarning("BRS-027: Missing period — skipping message {MessageId}", message.MessageId);
            return Task.CompletedTask;
        }

        var periodStart = DateTimeOffset.Parse(periodStartStr);
        var periodEnd = DateTimeOffset.Parse(periodEndStr);
        var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);

        var lines = new List<Brs027Handler.SettlementLineData>();
        if (payload.TryGetProperty("lines", out var linesArray))
        {
            foreach (var line in linesArray.EnumerateArray())
            {
                var chargeId = line.GetProperty("chargeId").GetString()!;
                var chargeType = line.GetProperty("chargeType").GetString()!;
                var ownerGln = line.GetProperty("ownerGln").GetString()!;
                var energyKwh = line.GetProperty("energyKwh").GetDecimal();
                var amountDkk = line.GetProperty("amountDkk").GetDecimal();
                var description = line.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                lines.Add(new Brs027Handler.SettlementLineData(
                    chargeId, chargeType, ownerGln, energyKwh, amountDkk, description));
            }
        }

        var result = Brs027Handler.ProcessWholesaleSettlement(
            gridArea, businessReason, periodStart, periodEnd, resolution, transactionId, lines);

        db.WholesaleSettlements.Add(result.Settlement);

        _logger.LogInformation(
            "BRS-027: Stored wholesale settlement for {GridArea} ({BusinessReason}), {Start}—{End}, {Count} lines, {Energy:F3} kWh, {Amount:F2} DKK",
            gridArea, businessReason,
            periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"),
            lines.Count, result.Settlement.TotalEnergyKwh, result.Settlement.TotalAmountDkk);

        return Task.CompletedTask;
    }

    private static string? GetPayloadString(JsonElement payload, string property)
    {
        if (payload.ValueKind == JsonValueKind.Undefined || payload.ValueKind == JsonValueKind.Null)
            return null;
        return payload.TryGetProperty(property, out var value) ? value.GetString() : null;
    }
}
