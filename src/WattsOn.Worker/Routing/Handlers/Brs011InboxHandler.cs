using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

/// <summary>
/// Inbox handler for BRS-011 — Incorrect Move.
/// Two scenarios:
/// 1. We're the INITIATOR — we reported the error. DataHub sends RSM-004 (D34 accept / D35 reject).
/// 2. We're the RECIPIENT (previous supplier) — DataHub sends RSM-003 (D33) asking us to resume supply.
/// </summary>
internal class Brs011InboxHandler
{
    private readonly ILogger _logger;
    public Brs011InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var businessReason = PayloadParser.GetString(payload, "businessReason") ?? "";
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");

        switch (businessReason)
        {
            case "D34": // Correction accepted
                await HandleCorrectionAccepted(db, payload, gsrnStr, ct);
                break;

            case "D35": // Correction rejected
                await HandleCorrectionRejected(db, payload, gsrnStr, ct);
                break;

            case "D33": // Resume supply request — we're the previous supplier
                await HandleResumeRequest(db, message, payload, gsrnStr, ct);
                break;

            default:
                _logger.LogInformation("BRS-011: Unhandled business reason '{Reason}'", businessReason);
                break;
        }
    }

    private async Task HandleCorrectionAccepted(WattsOnDbContext db, JsonElement payload, string? gsrnStr, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-011/D34: Missing GSRN");
            return;
        }

        var process = await FindInitiatorProcess(db, gsrnStr, ct);
        if (process is null)
        {
            _logger.LogWarning("BRS-011/D34: No active BRS-011 initiator process for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var correctionDateStr = PayloadParser.GetString(payload, "effectiveDate");
        var correctionDate = correctionDateStr != null
            ? DateTimeOffset.Parse(correctionDateStr)
            : process.EffectiveDate ?? DateTimeOffset.UtcNow;

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        var supply = mp != null ? await FindActiveSupply(db, mp.Id, ct) : null;

        Brs011Handler.HandleCorrectionAccepted(process, supply, correctionDate);
        _logger.LogInformation("BRS-011: Correction accepted for GSRN {Gsrn}", gsrnStr);
    }

    private async Task HandleCorrectionRejected(WattsOnDbContext db, JsonElement payload, string? gsrnStr, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-011/D35: Missing GSRN");
            return;
        }

        var process = await FindInitiatorProcess(db, gsrnStr, ct);
        if (process is null)
        {
            _logger.LogWarning("BRS-011/D35: No active BRS-011 initiator process for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var reason = PayloadParser.GetString(payload, "reason") ?? "Afvist af hidtidig leverandør";
        Brs011Handler.HandleCorrectionRejected(process, reason);
        _logger.LogInformation("BRS-011: Correction rejected for GSRN {Gsrn}: {Reason}", gsrnStr, reason);
    }

    private async Task HandleResumeRequest(WattsOnDbContext db, InboxMessage message, JsonElement payload, string? gsrnStr, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-011/D33: Missing GSRN");
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-011/D33: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var resumeDateStr = PayloadParser.GetString(payload, "resumeDate")
            ?? PayloadParser.GetString(payload, "effectiveDate");
        var resumeDate = resumeDateStr != null ? DateTimeOffset.Parse(resumeDateStr) : DateTimeOffset.UtcNow;

        var transactionId = PayloadParser.GetString(payload, "transactionId") ?? message.MessageId;
        var erroneousGlnStr = PayloadParser.GetString(payload, "erroneousSupplierGln") ?? message.SenderGln;
        var erroneousGln = GlnNumber.Create(erroneousGlnStr);

        // Find a customer linked to this MP
        var customer = await db.Supplies
            .Where(s => s.MeteringPointId == mp.Id)
            .OrderByDescending(s => s.SupplyPeriod.Start)
            .Select(s => s.CustomerId)
            .FirstOrDefaultAsync(ct);

        var customerEntity = customer != default
            ? await db.Customers.FindAsync(new object[] { customer }, ct)
            : null;

        if (customerEntity is null)
        {
            _logger.LogWarning("BRS-011/D33: No customer found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var result = Brs011Handler.HandleResumeRequest(
            gsrn, resumeDate, transactionId, erroneousGln, mp, customerEntity);

        db.Processes.Add(result.Process);
        db.Supplies.Add(result.NewSupply);
        _logger.LogInformation("BRS-011: Supply resumed for GSRN {Gsrn} from {Date}", gsrnStr, resumeDate.ToString("yyyy-MM-dd"));
    }

    private static async Task<Domain.Processes.BrsProcess?> FindInitiatorProcess(
        WattsOnDbContext db, string gsrnStr, CancellationToken ct)
    {
        return await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.FejlagtigFlytning)
            .Where(p => p.Role == ProcessRole.Initiator)
            .Where(p => p.MeteringPointGsrn != null && p.MeteringPointGsrn.Value == gsrnStr)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);
    }

    private static async Task<Domain.Entities.Supply?> FindActiveSupply(
        WattsOnDbContext db, Guid meteringPointId, CancellationToken ct)
    {
        return await db.Supplies
            .Where(s => s.MeteringPointId == meteringPointId)
            .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > DateTimeOffset.UtcNow)
            .OrderByDescending(s => s.SupplyPeriod.Start)
            .FirstOrDefaultAsync(ct);
    }
}
