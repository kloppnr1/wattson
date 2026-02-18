using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Pure domain service that calculates settlements from time series + prices.
/// No side effects, no persistence — just math.
///
/// Input: a time series (with observations) + a list of active price links (with prices + price points)
/// Output: an Settlement with SettlementLinjer
/// </summary>
public static class SettlementCalculator
{
    /// <summary>
    /// Calculate a settlement for the given time series and linked prices.
    /// Each price produces one SettlementLinje.
    /// </summary>
    public static Settlement Calculate(
        TimeSeries time_series,
        Supply supply,
        IReadOnlyList<PriceWithPoints> activePrices)
    {
        if (time_series.Observations.Count == 0)
            throw new InvalidOperationException("Cannot settle a time series with no observations.");

        var settlement = Settlement.Create(
            time_series.MeteringPointId,
            supply.Id,
            time_series.Period,
            time_series.Id,
            time_series.Version,
            time_series.TotalEnergy);

        foreach (var priceLink in activePrices)
        {
            var line = CalculateLine(settlement.Id, time_series, priceLink);
            if (line is not null)
                settlement.AddLine(line);
        }

        return settlement;
    }

    /// <summary>
    /// Calculate a correction (delta) settlement.
    /// Compares the new time series against the original settlement and produces
    /// line items with the difference amounts.
    /// </summary>
    public static Settlement CalculateCorrection(
        TimeSeries newTimeSeries,
        Supply supply,
        Settlement originalSettlement,
        IReadOnlyList<PriceWithPoints> activePrices)
    {
        if (newTimeSeries.Observations.Count == 0)
            throw new InvalidOperationException("Cannot settle a time series with no observations.");

        // Calculate what the full settlement would be with the new data
        var fullNewSettlement = Calculate(newTimeSeries, supply, activePrices);

        // The correction is a delta: new total - original total
        var deltaEnergy = newTimeSeries.TotalEnergy - originalSettlement.TotalEnergy;

        var correction = Settlement.CreateCorrection(
            newTimeSeries.MeteringPointId,
            supply.Id,
            newTimeSeries.Period,
            newTimeSeries.Id,
            newTimeSeries.Version,
            deltaEnergy,
            originalSettlement.Id);

        // Create delta lines: new line amount - original line amount per price
        foreach (var newLine in fullNewSettlement.Lines)
        {
            // Find matching original line by PriceId
            var originalLine = originalSettlement.Lines
                .FirstOrDefault(l => l.PriceId == newLine.PriceId);

            var originalAmount = originalLine?.Amount.Amount ?? 0m;
            var delta = newLine.Amount.Amount - originalAmount;

            if (delta == 0m) continue; // No change for this charge

            var originalQty = originalLine?.Quantity.Value ?? 0m;
            var deltaQty = newLine.Quantity.Value - originalQty;

            // Effective unit price from the new calculation
            var effectiveUnitPrice = deltaQty != 0m
                ? delta / deltaQty
                : newLine.UnitPrice;

            correction.AddLine(SettlementLinje.Create(
                correction.Id,
                newLine.PriceId,
                $"{newLine.Description} (justering)",
                EnergyQuantity.Create(deltaQty),
                effectiveUnitPrice));
        }

        return correction;
    }

    private static SettlementLinje? CalculateLine(
        Guid settlementId,
        TimeSeries time_series,
        PriceWithPoints priceLink)
    {
        var pris = priceLink.Price;

        return pris.Type switch
        {
            PriceType.Tarif => CalculateTariffLine(settlementId, time_series, priceLink),
            PriceType.Abonnement => CalculateSubscriptionLine(settlementId, time_series, priceLink),
            PriceType.Gebyr => null, // Fees are event-based, not settlement-based
            _ => null
        };
    }

    /// <summary>
    /// Tariff: for each observation, multiply energy × price at that hour.
    /// Supports both hourly-varying and flat tariffs.
    /// </summary>
    private static SettlementLinje CalculateTariffLine(
        Guid settlementId,
        TimeSeries time_series,
        PriceWithPoints priceLink)
    {
        var totalAmount = 0m;
        var totalEnergy = 0m;

        foreach (var obs in time_series.Observations)
        {
            var price = priceLink.GetPriceAt(obs.Timestamp);
            if (price is null) continue;

            totalAmount += obs.Quantity.Value * price.Value;
            totalEnergy += obs.Quantity.Value;
        }

        // Average unit price across the period
        var avgUnitPrice = totalEnergy != 0m ? totalAmount / totalEnergy : 0m;

        return SettlementLinje.Create(
            settlementId,
            priceLink.Price.Id,
            priceLink.Price.Description,
            EnergyQuantity.Create(totalEnergy),
            avgUnitPrice);
    }

    /// <summary>
    /// Subscription: flat daily fee × number of days in the settlement period.
    /// Not energy-based — uses count of days instead of kWh.
    /// </summary>
    private static SettlementLinje CalculateSubscriptionLine(
        Guid settlementId,
        TimeSeries time_series,
        PriceWithPoints priceLink)
    {
        var period = time_series.Period;
        var days = period.End.HasValue
            ? (decimal)(period.End.Value - period.Start).TotalDays
            : 30m; // Fallback for open-ended (shouldn't happen in settlement)

        var dailyPrice = priceLink.GetPriceAt(period.Start) ?? 0m;

        // For subscriptions, quantity represents days (not kWh)
        return SettlementLinje.Create(
            settlementId,
            priceLink.Price.Id,
            priceLink.Price.Description,
            EnergyQuantity.Create(days), // Using quantity field for day count
            dailyPrice);
    }
}

/// <summary>
/// Wraps a Price with its price points for settlement calculation.
/// Pre-loaded and sorted for efficient lookup.
/// </summary>
public class PriceWithPoints
{
    public Price Price { get; }
    private readonly List<(DateTimeOffset Timestamp, decimal Price)> _sortedPoints;

    public PriceWithPoints(Price pris)
    {
        Price = pris;
        _sortedPoints = pris.PricePoints
            .OrderBy(pp => pp.Timestamp)
            .Select(pp => (pp.Timestamp, pp.Price))
            .ToList();
    }

    /// <summary>
    /// Get the effective price at a specific timestamp.
    /// For tariffs: finds the most recent price point at or before the timestamp.
    /// For subscriptions: returns the single price.
    /// </summary>
    public decimal? GetPriceAt(DateTimeOffset timestamp)
    {
        if (_sortedPoints.Count == 0) return null;

        if (Price.Type == PriceType.Abonnement)
            return _sortedPoints[0].Price;

        // Binary search for the latest point <= timestamp
        decimal? result = null;
        foreach (var point in _sortedPoints)
        {
            if (point.Timestamp > timestamp) break;
            result = point.Price;
        }

        return result;
    }
}
