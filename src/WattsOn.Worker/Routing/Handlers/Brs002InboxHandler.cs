using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs002InboxHandler
{
    private readonly ILogger _logger;
    public Brs002InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-002: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        // Find the active BRS-002 process for this GSRN (Include Transitions so EF tracks new children correctly)
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.Supplyophør)
            .Where(p => p.MeteringPointGsrn != null && p.MeteringPointGsrn.Value == gsrnStr)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-002: No active process found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";
            Brs002Handler.HandleRejection(process, reason);
            _logger.LogInformation("BRS-002: Rejected for GSRN {Gsrn}: {Reason}", gsrnStr, reason);
            return;
        }

        // Confirmation — find active supply and end it
        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null) return;

        var supply = await db.Supplies
            .Where(s => s.MeteringPointId == mp.Id)
            .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > DateTimeOffset.UtcNow)
            .OrderByDescending(s => s.SupplyPeriod.Start)
            .FirstOrDefaultAsync(ct);

        if (supply is null)
        {
            _logger.LogWarning("BRS-002: No active supply for GSRN {Gsrn}", gsrnStr);
            process.TransitionTo("Completed", "No supply to end");
            process.MarkCompleted();
            return;
        }

        var actualEndDateStr = PayloadParser.GetString(payload, "actualEndDate");
        var actualEndDate = actualEndDateStr != null
            ? DateTimeOffset.Parse(actualEndDateStr)
            : process.EffectiveDate ?? DateTimeOffset.UtcNow;

        Brs002Handler.HandleConfirmation(process, supply, actualEndDate);
        _logger.LogInformation("BRS-002: Supply ended for GSRN {Gsrn} at {EndDate}", gsrnStr, actualEndDate.ToString("yyyy-MM-dd"));
    }
}
