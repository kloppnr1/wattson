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
/// Inbox handler for BRS-041 — Electrical Heating.
/// Handles DataHub responses:
/// - Rejection — marks process as rejected.
/// - Acceptance — marks process as accepted/completed, updates MP heating flag.
/// </summary>
internal class Brs041InboxHandler
{
    private readonly ILogger _logger;
    public Brs041InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            await HandleRejection(db, payload, ct);
            return;
        }

        // Electrical heating change accepted
        await HandleAcceptance(db, payload, ct);
    }

    private async Task HandleRejection(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";

        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.Elvarme)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-041: No active electrical heating process — rejection ignored");
            return;
        }

        Brs041Handler.HandleRejection(process, reason);
        _logger.LogInformation("BRS-041: Electrical heating change rejected: {Reason}", reason);
    }

    private async Task HandleAcceptance(WattsOnDbContext db, JsonElement payload, CancellationToken ct)
    {
        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.Elvarme)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is not null)
        {
            Brs041Handler.HandleAcceptance(process);

            // Update the metering point's electrical heating flag
            if (process.MeteringPointGsrn is not null)
            {
                var mp = await db.MeteringPoints
                    .FirstOrDefaultAsync(m => m.Gsrn == process.MeteringPointGsrn, ct);

                if (mp is not null)
                {
                    // Determine action from process data or payload
                    var action = PayloadParser.GetString(payload, "electricalHeating") ?? "";
                    var hasHeating = action.Equals("add", StringComparison.OrdinalIgnoreCase);
                    mp.SetElectricalHeating(hasHeating);
                    _logger.LogInformation("BRS-041: Updated MP {Gsrn} electrical heating to {HasHeating}",
                        process.MeteringPointGsrn.Value, hasHeating);
                }
            }

            _logger.LogInformation("BRS-041: Electrical heating change accepted");
        }
    }
}
