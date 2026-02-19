using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs008InboxHandler
{
    private readonly ILogger _logger;
    public Brs008InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-008: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-008: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var connectionStr = PayloadParser.GetString(payload, "connectionState");
        var newState = connectionStr != null
            ? Enum.Parse<ConnectionState>(connectionStr, ignoreCase: true)
            : ConnectionState.Tilsluttet;

        var result = Brs008Handler.Connect(mp, newState);

        if (result.WasChanged)
        {
            _logger.LogInformation("BRS-008: Connected GSRN {Gsrn} ({Previous} → {New})",
                gsrnStr, result.PreviousState, newState);
        }
        else
        {
            _logger.LogInformation("BRS-008: GSRN {Gsrn} already in state {State}", gsrnStr, newState);
        }
    }
}
