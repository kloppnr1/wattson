using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs036InboxHandler
{
    private readonly ILogger _logger;
    public Brs036InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-036: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var effectiveDateStr = PayloadParser.GetString(payload, "effectiveDate");
        var effectiveDate = effectiveDateStr != null
            ? DateTimeOffset.Parse(effectiveDateStr)
            : DateTimeOffset.UtcNow;

        // Parse obligation flag
        var hasObligation = false;
        if (payload.TryGetProperty("hasProductObligation", out var obligationEl))
        {
            hasObligation = obligationEl.ValueKind == JsonValueKind.True;
        }

        // Check if MP exists in our system (just for logging, not required)
        var mpExists = await db.MeteringPoints.AnyAsync(m => m.Gsrn.Value == gsrn.Value, ct);

        var data = new Brs036Handler.ProductObligationData(gsrn, hasObligation, effectiveDate);
        var result = Brs036Handler.RecordObligationChange(data, mpExists);

        db.Processes.Add(result.Process);

        _logger.LogInformation(
            "BRS-036: Product obligation {Status} for GSRN {Gsrn} (MP found: {Found})",
            hasObligation ? "set" : "removed", gsrnStr, mpExists);
    }
}
