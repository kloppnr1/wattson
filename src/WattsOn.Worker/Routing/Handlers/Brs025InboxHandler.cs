using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

/// <summary>
/// Inbox handler for BRS-025 — Request Historical Metered Data.
/// Handles DataHub responses:
/// - Rejection — marks process as rejected.
/// - Metered data (RSM-012) — routed to BRS-021 handler for time series processing.
///   The process is marked complete when data arrives.
/// </summary>
internal class Brs025InboxHandler
{
    private readonly ILogger _logger;
    public Brs025InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            await HandleRejection(db, payload, ct);
            return;
        }

        // Metered data response — route to BRS-021 handler for time series processing
        await MarkProcessComplete(db, ct);

        var brs021Handler = new Brs021InboxHandler(_logger);
        await brs021Handler.Handle(db, message, payload, ct);
    }

    private async Task HandleRejection(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";

        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.MåledataAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-025: No active metered data request process — rejection ignored");
            return;
        }

        Brs025Handler.HandleRejection(process, reason);
        _logger.LogInformation("BRS-025: Metered data request rejected: {Reason}", reason);
    }

    private async Task MarkProcessComplete(WattsOnDbContext db, CancellationToken ct)
    {
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.MåledataAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is not null)
        {
            Brs025Handler.HandleDataReceived(process);
            _logger.LogInformation("BRS-025: Metered data request completed — data received");
        }
    }
}
