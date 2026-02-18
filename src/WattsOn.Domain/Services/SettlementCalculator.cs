using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Pure domain service that calculates settlements from time series + prices.
/// No side effects, no persistence — just math.
///
/// Input: a time series (with observations) + a list of active price links (with prices + price points)
/// Output: an Afregning with AfregningLinjer
/// </summary>
public static class SettlementCalculator
{
    /// <summary>
    /// Calculate a settlement for the given time series and linked prices.
    /// Each price produces one AfregningLinje.
    /// </summary>
    public static Afregning Calculate(
        Tidsserie tidsserie,
        Leverance leverance,
        IReadOnlyList<PriceWithPoints> activePrices)
    {
        if (tidsserie.Observations.Count == 0)
            throw new InvalidOperationException("Cannot settle a time series with no observations.");

        var afregning = Afregning.Create(
            tidsserie.MålepunktId,
            leverance.Id,
            tidsserie.Period,
            tidsserie.Id,
            tidsserie.Version,
            tidsserie.TotalEnergy);

        foreach (var priceLink in activePrices)
        {
            var line = CalculateLine(afregning.Id, tidsserie, priceLink);
            if (line is not null)
                afregning.AddLine(line);
        }

        return afregning;
    }

    /// <summary>
    /// Calculate a correction (delta) settlement.
    /// Compares the new time series against the original settlement and produces
    /// line items with the difference amounts.
    /// </summary>
    public static Afregning CalculateCorrection(
        Tidsserie newTimeSeries,
        Leverance leverance,
        Afregning originalSettlement,
        IReadOnlyList<PriceWithPoints> activePrices)
    {
        if (newTimeSeries.Observations.Count == 0)
            throw new InvalidOperationException("Cannot settle a time series with no observations.");

        // Calculate what the full settlement would be with the new data
        var fullNewSettlement = Calculate(newTimeSeries, leverance, activePrices);

        // The correction is a delta: new total - original total
        var deltaEnergy = newTimeSeries.TotalEnergy - originalSettlement.TotalEnergy;

        var correction = Afregning.CreateCorrection(
            newTimeSeries.MålepunktId,
            leverance.Id,
            newTimeSeries.Period,
            newTimeSeries.Id,
            newTimeSeries.Version,
            deltaEnergy,
            originalSettlement.Id);

        // Create delta lines: new line amount - original line amount per price
        foreach (var newLine in fullNewSettlement.Lines)
        {
            // Find matching original line by PrisId
            var originalLine = originalSettlement.Lines
                .FirstOrDefault(l => l.PrisId == newLine.PrisId);

            var originalAmount = originalLine?.Amount.Amount ?? 0m;
            var delta = newLine.Amount.Amount - originalAmount;

            if (delta == 0m) continue; // No change for this charge

            var originalQty = originalLine?.Quantity.Value ?? 0m;
            var deltaQty = newLine.Quantity.Value - originalQty;

            // Effective unit price from the new calculation
            var effectiveUnitPrice = deltaQty != 0m
                ? delta / deltaQty
                : newLine.UnitPrice;

            correction.AddLine(AfregningLinje.Create(
                correction.Id,
                newLine.PrisId,
                $"{newLine.Description} (justering)",
                EnergyQuantity.Create(deltaQty),
                effectiveUnitPrice));
        }

        return correction;
    }

    private static AfregningLinje? CalculateLine(
        Guid afregningId,
        Tidsserie tidsserie,
        PriceWithPoints priceLink)
    {
        var pris = priceLink.Pris;

        return pris.Type switch
        {
            PriceType.Tarif => CalculateTariffLine(afregningId, tidsserie, priceLink),
            PriceType.Abonnement => CalculateSubscriptionLine(afregningId, tidsserie, priceLink),
            PriceType.Gebyr => null, // Fees are event-based, not settlement-based
            _ => null
        };
    }

    /// <summary>
    /// Tariff: for each observation, multiply energy × price at that hour.
    /// Supports both hourly-varying and flat tariffs.
    /// </summary>
    private static AfregningLinje CalculateTariffLine(
        Guid afregningId,
        Tidsserie tidsserie,
        PriceWithPoints priceLink)
    {
        var totalAmount = 0m;
        var totalEnergy = 0m;

        foreach (var obs in tidsserie.Observations)
        {
            var price = priceLink.GetPriceAt(obs.Timestamp);
            if (price is null) continue;

            totalAmount += obs.Quantity.Value * price.Value;
            totalEnergy += obs.Quantity.Value;
        }

        // Average unit price across the period
        var avgUnitPrice = totalEnergy != 0m ? totalAmount / totalEnergy : 0m;

        return AfregningLinje.Create(
            afregningId,
            priceLink.Pris.Id,
            priceLink.Pris.Description,
            EnergyQuantity.Create(totalEnergy),
            avgUnitPrice);
    }

    /// <summary>
    /// Subscription: flat daily fee × number of days in the settlement period.
    /// Not energy-based — uses count of days instead of kWh.
    /// </summary>
    private static AfregningLinje CalculateSubscriptionLine(
        Guid afregningId,
        Tidsserie tidsserie,
        PriceWithPoints priceLink)
    {
        var period = tidsserie.Period;
        var days = period.End.HasValue
            ? (decimal)(period.End.Value - period.Start).TotalDays
            : 30m; // Fallback for open-ended (shouldn't happen in settlement)

        var dailyPrice = priceLink.GetPriceAt(period.Start) ?? 0m;

        // For subscriptions, quantity represents days (not kWh)
        return AfregningLinje.Create(
            afregningId,
            priceLink.Pris.Id,
            priceLink.Pris.Description,
            EnergyQuantity.Create(days), // Using quantity field for day count
            dailyPrice);
    }
}

/// <summary>
/// Wraps a Pris with its price points for settlement calculation.
/// Pre-loaded and sorted for efficient lookup.
/// </summary>
public class PriceWithPoints
{
    public Pris Pris { get; }
    private readonly List<(DateTimeOffset Timestamp, decimal Price)> _sortedPoints;

    public PriceWithPoints(Pris pris)
    {
        Pris = pris;
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

        if (Pris.Type == PriceType.Abonnement)
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
