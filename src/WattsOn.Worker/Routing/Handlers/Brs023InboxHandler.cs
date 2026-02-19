using System.Text.Json;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs023InboxHandler
{
    private readonly ILogger _logger;
    public Brs023InboxHandler(ILogger logger) => _logger = logger;

    public Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gridArea = PayloadParser.GetString(payload, "gridArea") ?? "Unknown";
        var businessReason = PayloadParser.GetString(payload, "businessReason") ?? message.DocumentType;
        var mpType = PayloadParser.GetString(payload, "meteringPointType") ?? "E17";
        var settlementMethod = PayloadParser.GetString(payload, "settlementMethod");
        var periodStartStr = PayloadParser.GetString(payload, "periodStart");
        var periodEndStr = PayloadParser.GetString(payload, "periodEnd");
        var resolutionStr = PayloadParser.GetString(payload, "resolution") ?? "PT1H";
        var qualityStatus = PayloadParser.GetString(payload, "qualityStatus") ?? "Measured";
        var transactionId = PayloadParser.GetString(payload, "transactionId") ?? message.MessageId;

        if (periodStartStr is null || periodEndStr is null)
        {
            _logger.LogWarning("BRS-023: Missing period — skipping message {MessageId}", message.MessageId);
            return Task.CompletedTask;
        }

        var periodStart = DateTimeOffset.Parse(periodStartStr);
        var periodEnd = DateTimeOffset.Parse(periodEndStr);
        var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);

        var observations = new List<Brs023Handler.AggregatedObservationData>();
        if (payload.TryGetProperty("observations", out var obsArray))
        {
            foreach (var obs in obsArray.EnumerateArray())
            {
                var timestamp = DateTimeOffset.Parse(obs.GetProperty("timestamp").GetString()!);
                var kwh = obs.GetProperty("kwh").GetDecimal();
                observations.Add(new Brs023Handler.AggregatedObservationData(timestamp, kwh));
            }
        }

        var result = Brs023Handler.ProcessAggregatedData(
            gridArea, businessReason, mpType, settlementMethod,
            periodStart, periodEnd, resolution, qualityStatus, transactionId, observations);

        db.AggregatedTimeSeriesCollection.Add(result.TimeSeries);

        var label = Brs023Handler.MapBusinessReasonToLabel(businessReason);
        _logger.LogInformation(
            "BRS-023: Stored {Label} for {GridArea} ({MpType}/{Settlement}), {Start}—{End}, {Count} obs, {Total:F3} kWh",
            label, gridArea, mpType, settlementMethod ?? "all",
            periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"),
            observations.Count, result.TimeSeries.TotalEnergyKwh);

        return Task.CompletedTask;
    }
}
