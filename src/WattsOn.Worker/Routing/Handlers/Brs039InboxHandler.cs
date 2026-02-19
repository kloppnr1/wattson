using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

/// <summary>
/// Inbox handler for BRS-039 — Service Request.
/// Handles DataHub responses:
/// - Rejection — marks process as rejected.
/// - Acceptance — marks process as accepted/completed.
/// </summary>
internal class Brs039InboxHandler
{
    private readonly ILogger _logger;
    public Brs039InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            await HandleRejection(db, payload, ct);
            return;
        }

        // Service request accepted
        await HandleAcceptance(db, ct);
    }

    private async Task HandleRejection(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by grid company";

        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.Serviceydelse)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-039: No active service request process — rejection ignored");
            return;
        }

        Brs039Handler.HandleRejection(process, reason);
        _logger.LogInformation("BRS-039: Service request rejected: {Reason}", reason);
    }

    private async Task HandleAcceptance(WattsOnDbContext db, CancellationToken ct)
    {
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.Serviceydelse)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is not null)
        {
            Brs039Handler.HandleAcceptance(process);
            _logger.LogInformation("BRS-039: Service request accepted");
        }
    }
}
