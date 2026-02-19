using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

/// <summary>
/// Inbox handler for BRS-038 — Request for Charge Links.
/// Handles DataHub responses:
/// - Rejection (RSM-032 with error) — marks process as rejected.
/// - Charge link data (RSM-031/E0G with D17 format) — routed to BRS-031 handler.
/// </summary>
internal class Brs038InboxHandler
{
    private readonly ILogger _logger;
    public Brs038InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var businessReason = PayloadParser.GetString(payload, "businessReason") ?? "";
        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            await HandleRejection(db, payload, ct);
            return;
        }

        // Charge link data — route to BRS-031 handler (D17 format)
        if (businessReason is "D17" or "E0G")
        {
            await MarkProcessComplete(db, ct);

            var brs031Handler = new Brs031InboxHandler(_logger);
            await brs031Handler.Handle(db, message, payload, ct);
            return;
        }

        _logger.LogInformation("BRS-038: Unhandled business reason '{Reason}'", businessReason);
    }

    private async Task HandleRejection(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";

        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.PristilknytningAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-038: No active charge link request process — rejection ignored");
            return;
        }

        Brs038Handler.HandleRejection(process, reason);
        _logger.LogInformation("BRS-038: Charge link request rejected: {Reason}", reason);
    }

    private async Task MarkProcessComplete(WattsOnDbContext db, CancellationToken ct)
    {
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.PristilknytningAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is not null)
        {
            Brs038Handler.HandleDataReceived(process);
            _logger.LogInformation("BRS-038: Charge link request completed — data received");
        }
    }
}
