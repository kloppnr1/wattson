using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs006InboxHandler
{
    private readonly ILogger _logger;
    public Brs006InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-006: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-006: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        // Parse optional update fields
        var typeStr = PayloadParser.GetString(payload, "type");
        var artStr = PayloadParser.GetString(payload, "art");
        var settlementStr = PayloadParser.GetString(payload, "settlementMethod");
        var resolutionStr = PayloadParser.GetString(payload, "resolution");
        var connectionStr = PayloadParser.GetString(payload, "connectionState");
        var gridArea = PayloadParser.GetString(payload, "gridArea");
        var gridCompanyGlnStr = PayloadParser.GetString(payload, "gridCompanyGln");

        // Parse address if present
        Address? address = null;
        if (payload.TryGetProperty("address", out var addrEl) && addrEl.ValueKind == JsonValueKind.Object)
        {
            var street = addrEl.TryGetProperty("streetName", out var s) ? s.GetString() ?? "" : "";
            var building = addrEl.TryGetProperty("buildingNumber", out var b) ? b.GetString() ?? "" : "";
            var postCode = addrEl.TryGetProperty("postCode", out var pc) ? pc.GetString() ?? "" : "";
            var city = addrEl.TryGetProperty("cityName", out var ci) ? ci.GetString() ?? "" : "";
            var floor = addrEl.TryGetProperty("floor", out var fl) ? fl.GetString() : null;
            var suite = addrEl.TryGetProperty("suite", out var su) ? su.GetString() : null;
            address = Address.Create(street, building, postCode, city, floor, suite);
        }

        var update = new Brs006Handler.MasterDataUpdate(
            Type: typeStr != null ? Enum.Parse<MeteringPointType>(typeStr, ignoreCase: true) : null,
            Art: artStr != null ? Enum.Parse<MeteringPointCategory>(artStr, ignoreCase: true) : null,
            SettlementMethod: settlementStr != null ? Enum.Parse<SettlementMethod>(settlementStr, ignoreCase: true) : null,
            Resolution: resolutionStr != null ? Enum.Parse<Resolution>(resolutionStr, ignoreCase: true) : null,
            ConnectionState: connectionStr != null ? Enum.Parse<ConnectionState>(connectionStr, ignoreCase: true) : null,
            GridArea: gridArea,
            GridCompanyGln: gridCompanyGlnStr != null ? GlnNumber.Create(gridCompanyGlnStr) : null,
            Address: address);

        var result = Brs006Handler.ApplyMasterDataUpdate(mp, update);

        if (result.ChangedFields.Count > 0)
        {
            _logger.LogInformation("BRS-006: Updated GSRN {Gsrn}: {Fields}",
                gsrnStr, string.Join(", ", result.ChangedFields));
        }
        else
        {
            _logger.LogInformation("BRS-006: No changes for GSRN {Gsrn}", gsrnStr);
        }
    }
}
