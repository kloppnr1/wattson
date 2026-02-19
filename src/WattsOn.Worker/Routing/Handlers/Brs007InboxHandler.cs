using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs007InboxHandler
{
    private readonly ILogger _logger;
    public Brs007InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-007: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-007: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var effectiveDateStr = PayloadParser.GetString(payload, "effectiveDate");
        var effectiveDate = effectiveDateStr != null
            ? DateTimeOffset.Parse(effectiveDateStr)
            : DateTimeOffset.UtcNow;
        var reason = PayloadParser.GetString(payload, "reason");

        // Find active supply
        var activeSupply = await db.Supplies
            .Where(s => s.MeteringPointId == mp.Id)
            .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > effectiveDate)
            .FirstOrDefaultAsync(ct);

        var data = new Brs007Handler.DecommissionData(gsrn, effectiveDate, reason);
        var result = Brs007Handler.Decommission(mp, data, activeSupply);

        db.Processes.Add(result.Process);

        _logger.LogInformation(
            "BRS-007: Decommissioned GSRN {Gsrn} (was {PreviousState}), supply ended: {SupplyEnded}",
            gsrnStr, result.PreviousState, result.EndedSupply is not null);
    }
}
