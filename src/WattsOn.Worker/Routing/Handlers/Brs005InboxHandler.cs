using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

/// <summary>
/// Inbox handler for BRS-005 — Request Master Data.
/// Handles DataHub responses:
/// - Rejection — marks process as rejected.
/// - Master data (RSM-022) — routed to BRS-006 handler for processing.
///   The process is marked complete when data arrives.
/// </summary>
internal class Brs005InboxHandler
{
    private readonly ILogger _logger;
    public Brs005InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            await HandleRejection(db, payload, ct);
            return;
        }

        // Master data response — route to BRS-006 handler for actual processing
        await MarkProcessComplete(db, ct);

        var brs006Handler = new Brs006InboxHandler(_logger);
        await brs006Handler.Handle(db, message, payload, ct);
    }

    private async Task HandleRejection(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";

        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.StamdataAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-005: No active master data request process — rejection ignored");
            return;
        }

        Brs005Handler.HandleRejection(process, reason);
        _logger.LogInformation("BRS-005: Master data request rejected: {Reason}", reason);
    }

    private async Task MarkProcessComplete(WattsOnDbContext db, CancellationToken ct)
    {
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.StamdataAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is not null)
        {
            Brs005Handler.HandleDataReceived(process);
            _logger.LogInformation("BRS-005: Master data request completed — data received");
        }
    }
}
