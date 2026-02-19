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

internal class Brs004InboxHandler
{
    private readonly ILogger _logger;
    public Brs004InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-004: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);

        // Parse required fields
        var typeStr = PayloadParser.GetString(payload, "type");
        var artStr = PayloadParser.GetString(payload, "art");
        var settlementStr = PayloadParser.GetString(payload, "settlementMethod");
        var resolutionStr = PayloadParser.GetString(payload, "resolution");
        var connectionStr = PayloadParser.GetString(payload, "connectionState");
        var gridArea = PayloadParser.GetString(payload, "gridArea");
        var gridCompanyGlnStr = PayloadParser.GetString(payload, "gridCompanyGln");
        var parentGsrnStr = PayloadParser.GetString(payload, "parentGsrn");

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

        var data = new Brs004Handler.NewMeteringPointData(
            Gsrn: gsrn,
            Type: typeStr != null ? Enum.Parse<MeteringPointType>(typeStr, ignoreCase: true) : MeteringPointType.Forbrug,
            Art: artStr != null ? Enum.Parse<MeteringPointCategory>(artStr, ignoreCase: true) : MeteringPointCategory.Fysisk,
            SettlementMethod: settlementStr != null ? Enum.Parse<SettlementMethod>(settlementStr, ignoreCase: true) : SettlementMethod.Flex,
            Resolution: resolutionStr != null ? Enum.Parse<Resolution>(resolutionStr, ignoreCase: true) : Resolution.PT1H,
            GridArea: gridArea ?? "DK1",
            GridCompanyGln: gridCompanyGlnStr != null ? GlnNumber.Create(gridCompanyGlnStr) : GlnNumber.FromTrusted("5790000000005"),
            ConnectionState: connectionStr != null ? Enum.Parse<ConnectionState>(connectionStr, ignoreCase: true) : ConnectionState.Ny,
            Address: address,
            ParentGsrn: parentGsrnStr != null ? Gsrn.Create(parentGsrnStr) : null);

        // Check if the metering point already exists (upsert)
        var existingMp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);

        if (existingMp is not null)
        {
            var result = Brs004Handler.UpdateExistingMeteringPoint(existingMp, data);
            _logger.LogInformation("BRS-004: Updated existing GSRN {Gsrn}: {Fields}",
                gsrnStr, string.Join(", ", result.ChangedFields));
        }
        else
        {
            var result = Brs004Handler.CreateMeteringPoint(data);
            db.MeteringPoints.Add(result.MeteringPoint);
            _logger.LogInformation("BRS-004: Created new metering point GSRN {Gsrn}", gsrnStr);
        }
    }
}
