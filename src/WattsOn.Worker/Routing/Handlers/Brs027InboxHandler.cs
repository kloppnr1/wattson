using System.Text.Json;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs027InboxHandler
{
    private readonly ILogger _logger;
    public Brs027InboxHandler(ILogger logger) => _logger = logger;

    public Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gridArea = PayloadParser.GetString(payload, "gridArea") ?? "Unknown";
        var businessReason = PayloadParser.GetString(payload, "businessReason") ?? "D05";
        var periodStartStr = PayloadParser.GetString(payload, "periodStart");
        var periodEndStr = PayloadParser.GetString(payload, "periodEnd");
        var resolutionStr = PayloadParser.GetString(payload, "resolution") ?? "PT1H";
        var transactionId = PayloadParser.GetString(payload, "transactionId") ?? message.MessageId;

        if (periodStartStr is null || periodEndStr is null)
        {
            _logger.LogWarning("BRS-027: Missing period — skipping message {MessageId}", message.MessageId);
            return Task.CompletedTask;
        }

        var periodStart = DateTimeOffset.Parse(periodStartStr);
        var periodEnd = DateTimeOffset.Parse(periodEndStr);
        var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);

        var lines = new List<Brs027Handler.SettlementLineData>();
        if (payload.TryGetProperty("lines", out var linesArray))
        {
            foreach (var line in linesArray.EnumerateArray())
            {
                var chargeId = line.GetProperty("chargeId").GetString()!;
                var chargeType = line.GetProperty("chargeType").GetString()!;
                var ownerGln = line.GetProperty("ownerGln").GetString()!;
                var energyKwh = line.GetProperty("energyKwh").GetDecimal();
                var amountDkk = line.GetProperty("amountDkk").GetDecimal();
                var description = line.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                lines.Add(new Brs027Handler.SettlementLineData(
                    chargeId, chargeType, ownerGln, energyKwh, amountDkk, description));
            }
        }

        var result = Brs027Handler.ProcessWholesaleSettlement(
            gridArea, businessReason, periodStart, periodEnd, resolution, transactionId, lines);

        db.WholesaleSettlements.Add(result.Settlement);

        _logger.LogInformation(
            "BRS-027: Stored wholesale settlement for {GridArea} ({BusinessReason}), {Start}—{End}, {Count} lines, {Energy:F3} kWh, {Amount:F2} DKK",
            gridArea, businessReason,
            periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"),
            lines.Count, result.Settlement.TotalEnergyKwh, result.Settlement.TotalAmountDkk);

        return Task.CompletedTask;
    }
}
