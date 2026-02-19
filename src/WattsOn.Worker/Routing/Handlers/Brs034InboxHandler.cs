using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

/// <summary>
/// Inbox handler for BRS-034 — Request for Prices.
/// Handles DataHub responses:
/// - Rejection (RSM-035 with error) — marks process as rejected.
/// - Price data (RSM-034/D18/D08) — routed to BRS-031 handler for processing.
///   The process is marked complete when data arrives.
/// </summary>
internal class Brs034InboxHandler
{
    private readonly ILogger _logger;
    public Brs034InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var businessReason = PayloadParser.GetString(payload, "businessReason") ?? "";
        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            await HandleRejection(db, payload, ct);
            return;
        }

        // Price data response — route to BRS-031 handler for actual processing
        // The response uses the same D18/D08 format as regular price updates
        if (businessReason is "D18" or "D08" or "E0G" or "D48")
        {
            // Mark the BRS-034 process as complete
            await MarkProcessComplete(db, ct);

            // Route to BRS-031 handler for actual data processing
            var brs031Handler = new Brs031InboxHandler(_logger);
            await brs031Handler.Handle(db, message, payload, ct);
            return;
        }

        _logger.LogInformation("BRS-034: Unhandled business reason '{Reason}'", businessReason);
    }

    private async Task HandleRejection(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";

        // Find the most recent pending BRS-034 process
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.PrisAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-034: No active price request process — rejection ignored");
            return;
        }

        Brs034Handler.HandleRejection(process, reason);
        _logger.LogInformation("BRS-034: Price request rejected: {Reason}", reason);
    }

    private async Task MarkProcessComplete(WattsOnDbContext db, CancellationToken ct)
    {
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.PrisAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is not null)
        {
            Brs034Handler.HandleDataReceived(process);
            _logger.LogInformation("BRS-034: Price request completed — data received");
        }
    }
}
