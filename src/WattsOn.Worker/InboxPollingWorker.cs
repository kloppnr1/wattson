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
/// Supports BRS-001 (Leverandørskift) and BRS-009 (Tilflytning/Fraflytning) messages.
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

            case "BRS-009":
                await HandleBrs009Message(db, message, payload, ct);
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
                                        process, mp, customer, identity.Id, supply);

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

    private static string? GetPayloadString(JsonElement payload, string property)
    {
        if (payload.ValueKind == JsonValueKind.Undefined || payload.ValueKind == JsonValueKind.Null)
            return null;
        return payload.TryGetProperty(property, out var value) ? value.GetString() : null;
    }
}
