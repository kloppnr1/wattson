using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs015InboxHandler
{
    private readonly ILogger _logger;
    public Brs015InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-015: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var process = await db.Processes
            .Include(p => p.Transitions)
            .Where(p => p.ProcessType == ProcessType.CustomerStamdataOpdatering)
            .Where(p => p.MeteringPointGsrn != null && p.MeteringPointGsrn.Value == gsrnStr)
            .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
            .OrderByDescending(p => p.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (process is null)
        {
            _logger.LogWarning("BRS-015: No active process found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var isRejection = payload.TryGetProperty("rejected", out var rej) && rej.GetBoolean();

        if (isRejection)
        {
            var reason = PayloadParser.GetString(payload, "reason") ?? "Rejected by DataHub";
            Brs015Handler.HandleRejection(process, reason);
            _logger.LogInformation("BRS-015: Rejected for GSRN {Gsrn}: {Reason}", gsrnStr, reason);
        }
        else
        {
            Brs015Handler.HandleConfirmation(process);
            _logger.LogInformation("BRS-015: Customer data update confirmed for GSRN {Gsrn}", gsrnStr);
        }
    }
}
