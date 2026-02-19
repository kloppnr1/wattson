using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs021InboxHandler
{
    private readonly ILogger _logger;
    public Brs021InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-021: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-021: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var periodStartStr = PayloadParser.GetString(payload, "periodStart");
        var periodEndStr = PayloadParser.GetString(payload, "periodEnd");
        if (periodStartStr is null || periodEndStr is null)
        {
            _logger.LogWarning("BRS-021: Missing period for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var periodStart = DateTimeOffset.Parse(periodStartStr);
        var periodEnd = DateTimeOffset.Parse(periodEndStr);
        var resolutionStr = PayloadParser.GetString(payload, "resolution") ?? "PT1H";
        var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);
        var transactionId = PayloadParser.GetString(payload, "transactionId") ?? message.MessageId;

        // Parse observations
        var observations = new List<Brs021Handler.ObservationData>();
        if (payload.TryGetProperty("observations", out var obsArray))
        {
            foreach (var obs in obsArray.EnumerateArray())
            {
                var timestamp = DateTimeOffset.Parse(obs.GetProperty("timestamp").GetString()!);
                var kwh = obs.GetProperty("kwh").GetDecimal();
                var qualityCode = obs.TryGetProperty("quality", out var q) ? q.GetString() : null;
                var quality = Brs021Handler.MapQuantityStatus(qualityCode);
                observations.Add(new Brs021Handler.ObservationData(timestamp, kwh, quality));
            }
        }

        if (observations.Count == 0)
        {
            _logger.LogWarning("BRS-021: No observations in message for GSRN {Gsrn}", gsrnStr);
            return;
        }

        // Find existing latest time series for the same period
        var existingLatest = await db.TimeSeriesCollection
            .Where(t => t.MeteringPointId == mp.Id)
            .Where(t => t.Period.Start == periodStart && t.Period.End == periodEnd)
            .Where(t => t.IsLatest)
            .FirstOrDefaultAsync(ct);

        var result = Brs021Handler.ProcessMeteredData(
            mp.Id, periodStart, periodEnd, resolution, observations, transactionId, existingLatest);

        db.TimeSeriesCollection.Add(result.TimeSeries);

        _logger.LogInformation(
            "BRS-021: {Action} time series for GSRN {Gsrn}, period {Start}—{End}, v{Version}, {Count} observations, {TotalKwh:F3} kWh",
            result.SupersededVersion is not null ? "Updated" : "Created",
            gsrnStr,
            periodStart.ToString("yyyy-MM-dd"),
            periodEnd.ToString("yyyy-MM-dd"),
            result.TimeSeries.Version,
            observations.Count,
            result.TimeSeries.TotalEnergy.Value);
    }
}
