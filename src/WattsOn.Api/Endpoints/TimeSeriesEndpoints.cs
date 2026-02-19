using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class TimeSeriesEndpoints
{
    public static WebApplication MapTimeSeriesEndpoints(this WebApplication app)
    {
        /// <summary>
        /// Ingest a time series with observations for a metering point.
        /// If an existing time series covers the same period, it becomes a new version
        /// (the old one is marked as superseded), which triggers correction detection.
        /// </summary>
        app.MapPost("/api/time-series", async (CreateTimeSeriesRequest req, WattsOnDbContext db) =>
        {
            // Validate metering point exists
            var mp = await db.MeteringPoints.FindAsync(req.MeteringPointId);
            if (mp is null) return Results.BadRequest(new { error = "MeteringPoint not found" });

            var period = Period.Create(req.PeriodStart, req.PeriodEnd);
            var resolution = Enum.Parse<Resolution>(req.Resolution);

            // Check for existing time series for the same period → new version
            var existing = await db.TimeSeriesCollection
                .Where(t => t.MeteringPointId == req.MeteringPointId)
                .Where(t => t.Period.Start == req.PeriodStart && t.Period.End == req.PeriodEnd)
                .Where(t => t.IsLatest)
                .FirstOrDefaultAsync();

            var version = 1;
            if (existing is not null)
            {
                existing.Supersede();
                version = existing.Version + 1;
            }

            var time_series = TimeSeries.Create(req.MeteringPointId, period, resolution, version, req.TransactionId);

            foreach (var obs in req.Observations)
            {
                var quality = Enum.Parse<QuantityQuality>(obs.Quality ?? "Målt");
                time_series.AddObservation(obs.Timestamp, EnergyQuantity.Create(obs.KWh), quality);
            }

            db.TimeSeriesCollection.Add(time_series);
            await db.SaveChangesAsync();

            return Results.Created($"/api/time_series/{time_series.Id}", new
            {
                time_series.Id,
                time_series.MeteringPointId,
                PeriodStart = time_series.Period.Start,
                PeriodEnd = time_series.Period.End,
                Resolution = time_series.Resolution.ToString(),
                time_series.Version,
                time_series.IsLatest,
                ObservationCount = time_series.Observations.Count,
                TotalEnergyKwh = time_series.TotalEnergy.Value
            });
        }).WithName("CreateTimeSeries");

        app.MapGet("/api/time_series/{id:guid}", async (Guid id, WattsOnDbContext db) =>
        {
            var ts = await db.TimeSeriesCollection
                .Include(t => t.Observations.OrderBy(o => o.Timestamp))
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (ts is null) return Results.NotFound();

            return Results.Ok(new
            {
                ts.Id,
                ts.MeteringPointId,
                PeriodStart = ts.Period.Start,
                PeriodEnd = ts.Period.End,
                Resolution = ts.Resolution.ToString(),
                ts.Version,
                ts.IsLatest,
                ts.TransactionId,
                ts.ReceivedAt,
                TotalEnergyKwh = ts.TotalEnergy.Value,
                Observations = ts.Observations.Select(o => new
                {
                    o.Timestamp,
                    KWh = o.Quantity.Value,
                    Quality = o.Quality.ToString()
                })
            });
        }).WithName("GetTimeSeries");

        return app;
    }
}
