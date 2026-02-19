using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs013InboxHandler
{
    private readonly ILogger _logger;
    public Brs013InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-013: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-013: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var connectionStr = PayloadParser.GetString(payload, "connectionState");
        var newState = Brs013Handler.ParseConnectionState(connectionStr);

        if (newState is null)
        {
            _logger.LogWarning("BRS-013: Unknown connection state '{State}' for GSRN {Gsrn}",
                connectionStr, gsrnStr);
            return;
        }

        var result = Brs013Handler.UpdateConnectionState(mp, newState.Value);

        if (result.WasChanged)
        {
            _logger.LogInformation("BRS-013: GSRN {Gsrn} state changed {Previous} → {New}",
                gsrnStr, result.PreviousState, result.NewState);
        }
        else
        {
            _logger.LogInformation("BRS-013: GSRN {Gsrn} already in state {State}", gsrnStr, newState);
        }
    }
}
