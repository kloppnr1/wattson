using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

/// <summary>
/// Inbox handler for BRS-024 — Request Yearly Consumption Sum.
/// Handles DataHub responses:
/// - Rejection — marks process as rejected.
/// - Yearly sum data (RSM-012) — stores data and marks process complete.
/// </summary>
internal class Brs024InboxHandler
{
    private readonly ILogger _logger;
    public Brs024InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            await HandleRejection(db, payload, ct);
            return;
        }

        // Yearly sum data received — mark process complete
        // Data can be stored/forwarded as needed
        await MarkProcessComplete(db, ct);
        _logger.LogInformation("BRS-024: Yearly sum data received");
    }

    private async Task HandleRejection(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";

        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.ÅrssumAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-024: No active yearly sum request process — rejection ignored");
            return;
        }

        Brs024Handler.HandleRejection(process, reason);
        _logger.LogInformation("BRS-024: Yearly sum request rejected: {Reason}", reason);
    }

    private async Task MarkProcessComplete(WattsOnDbContext db, CancellationToken ct)
    {
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.ÅrssumAnmodning)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is not null)
        {
            Brs024Handler.HandleDataReceived(process);
            _logger.LogInformation("BRS-024: Yearly sum request completed — data received");
        }
    }
}
