using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Pure domain service that calculates settlements from time series + prices.
/// No side effects, no persistence — just math.
///
/// Takes three separate price sources:
/// 1. DataHub charges (nettarif, systemtarif, etc.) — via PriceWithPoints
/// 2. Spot prices — flat list of (timestamp, price) from Energi Data Service
/// 3. Supplier margin — single active rate (from ValidFrom) for the product
///
/// PricingModel determines how electricity cost is calculated:
/// - SpotAddon: spot line (hourly) + margin addon line (flat)
/// - Fixed: single electricity line at fixed rate (no spot)
/// </summary>
public static class SettlementCalculator
{
    /// <summary>
    /// Calculate a settlement for the given time series and price sources.
    /// </summary>
    public static Settlement Calculate(
        TimeSeries timeSeries,
        Supply supply,
        IReadOnlyList<PriceWithPoints> datahubPrices,
        IReadOnlyList<SpotPrice> spotPrices,
        SupplierMargin? activeMargin = null,
        PricingModel pricingModel = PricingModel.SpotAddon)
    {
        if (timeSeries.Observations.Count == 0)
            throw new InvalidOperationException("Cannot settle a time series with no observations.");

        var settlement = Settlement.Create(
            timeSeries.MeteringPointId,
            supply.Id,
            timeSeries.Period,
            timeSeries.Id,
            timeSeries.Version,
            timeSeries.TotalEnergy);

        // DataHub charge lines (same regardless of pricing model)
        foreach (var priceLink in datahubPrices)
        {
            var line = CalculateDataHubLine(settlement.Id, timeSeries, priceLink);
            if (line is not null)
                settlement.AddLine(line);
        }

        // Electricity cost lines — depend on pricing model
        switch (pricingModel)
        {
            case PricingModel.Fixed:
                // Fixed: single line at fixed rate, no spot prices
                var fixedLine = CalculateFixedElectricityLine(settlement.Id, timeSeries, activeMargin);
                if (fixedLine is not null)
                    settlement.AddLine(fixedLine);
                break;

            case PricingModel.SpotAddon:
            default:
                // Spot + addon margin
                var spotLine = CalculateSpotLine(settlement.Id, timeSeries, spotPrices);
                if (spotLine is not null)
                    settlement.AddLine(spotLine);

                var marginLine = CalculateMarginAddonLine(settlement.Id, timeSeries, activeMargin);
                if (marginLine is not null)
                    settlement.AddLine(marginLine);
                break;
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
        IReadOnlyList<PriceWithPoints> datahubPrices,
        IReadOnlyList<SpotPrice> spotPrices,
        SupplierMargin? activeMargin = null,
        PricingModel pricingModel = PricingModel.SpotAddon)
    {
        if (newTimeSeries.Observations.Count == 0)
            throw new InvalidOperationException("Cannot settle a time series with no observations.");

        // Calculate what the full settlement would be with the new data
        var fullNewSettlement = Calculate(newTimeSeries, supply, datahubPrices, spotPrices, activeMargin, pricingModel);

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

        // Create delta lines: match by (Source, PriceId) composite key
        foreach (var newLine in fullNewSettlement.Lines)
        {
            var originalLine = originalSettlement.Lines
                .FirstOrDefault(l => l.Source == newLine.Source && l.PriceId == newLine.PriceId);

            var originalAmount = originalLine?.Amount.Amount ?? 0m;
            var delta = newLine.Amount.Amount - originalAmount;

            if (delta == 0m) continue;

            var originalQty = originalLine?.Quantity.Value ?? 0m;
            var deltaQty = newLine.Quantity.Value - originalQty;

            var effectiveUnitPrice = deltaQty != 0m
                ? delta / deltaQty
                : newLine.UnitPrice;

            var correctionLine = newLine.Source switch
            {
                SettlementLineSource.SpotPrice => SettlementLine.CreateSpot(
                    correction.Id,
                    $"{newLine.Description} (justering)",
                    EnergyQuantity.Create(deltaQty),
                    effectiveUnitPrice),
                SettlementLineSource.SupplierMargin => SettlementLine.CreateMargin(
                    correction.Id,
                    $"{newLine.Description} (justering)",
                    EnergyQuantity.Create(deltaQty),
                    effectiveUnitPrice),
                _ => SettlementLine.Create(
                    correction.Id,
                    newLine.PriceId!.Value,
                    $"{newLine.Description} (justering)",
                    EnergyQuantity.Create(deltaQty),
                    effectiveUnitPrice),
            };

            correction.AddLine(correctionLine);
        }

        return correction;
    }

    // --- DataHub charges ---

    private static SettlementLine? CalculateDataHubLine(
        Guid settlementId,
        TimeSeries timeSeries,
        PriceWithPoints priceLink)
    {
        var price = priceLink.Price;

        return price.Type switch
        {
            PriceType.Tarif => CalculateTariffLine(settlementId, timeSeries, priceLink),
            PriceType.Abonnement => CalculateSubscriptionLine(settlementId, timeSeries, priceLink),
            PriceType.Gebyr => null, // Fees are event-based, not settlement-based
            _ => null
        };
    }

    /// <summary>
    /// Tariff: for each observation, multiply energy × price at that hour.
    /// Supports both hourly-varying and flat tariffs.
    /// When time series is hourly (PT1H) but price is sub-hourly (PT15M),
    /// averages the sub-hourly price points within each hour.
    /// </summary>
    private static SettlementLine CalculateTariffLine(
        Guid settlementId,
        TimeSeries timeSeries,
        PriceWithPoints priceLink)
    {
        var totalAmount = 0m;
        var totalEnergy = 0m;
        var needsAveraging = timeSeries.Resolution == Resolution.PT1H
            && priceLink.Price.PriceResolution == Resolution.PT15M;

        foreach (var obs in timeSeries.Observations)
        {
            decimal? price;
            if (needsAveraging)
                price = priceLink.GetAveragePriceInHour(obs.Timestamp);
            else
                price = priceLink.GetPriceAt(obs.Timestamp);

            if (price is null) continue;

            totalAmount += obs.Quantity.Value * price.Value;
            totalEnergy += obs.Quantity.Value;
        }

        var avgUnitPrice = totalEnergy != 0m ? totalAmount / totalEnergy : 0m;

        return SettlementLine.Create(
            settlementId,
            priceLink.Price.Id,
            priceLink.Price.Description,
            EnergyQuantity.Create(totalEnergy),
            avgUnitPrice);
    }

    /// <summary>
    /// Subscription: flat daily fee × number of days in the settlement period.
    /// </summary>
    private static SettlementLine CalculateSubscriptionLine(
        Guid settlementId,
        TimeSeries timeSeries,
        PriceWithPoints priceLink)
    {
        var period = timeSeries.Period;
        var days = period.End.HasValue
            ? (decimal)(period.End.Value - period.Start).TotalDays
            : 30m;

        var dailyPrice = priceLink.GetPriceAt(period.Start) ?? 0m;

        return SettlementLine.Create(
            settlementId,
            priceLink.Price.Id,
            priceLink.Price.Description,
            EnergyQuantity.Create(days),
            dailyPrice);
    }

    // --- Spot prices (SpotAddon products only) ---

    /// <summary>
    /// Spot price: for each observation, multiply energy × spot price at that timestamp.
    /// Averages sub-hourly (PT15M) prices into hourly when time series is PT1H.
    /// </summary>
    private static SettlementLine? CalculateSpotLine(
        Guid settlementId,
        TimeSeries timeSeries,
        IReadOnlyList<SpotPrice> spotPrices)
    {
        if (spotPrices.Count == 0) return null;

        var lookup = spotPrices.ToDictionary(s => s.Timestamp);
        var totalAmount = 0m;
        var totalEnergy = 0m;

        foreach (var obs in timeSeries.Observations)
        {
            decimal price;
            if (timeSeries.Resolution == Resolution.PT1H)
            {
                // Average 4 quarter-hour spots within this hour
                var sum = 0m;
                var count = 0;
                for (var i = 0; i < 4; i++)
                {
                    if (lookup.TryGetValue(obs.Timestamp.AddMinutes(i * 15), out var sp))
                    {
                        sum += sp.PriceDkkPerKwh;
                        count++;
                    }
                }
                price = count > 0 ? sum / count : 0m;
            }
            else
            {
                price = lookup.TryGetValue(obs.Timestamp, out var sp) ? sp.PriceDkkPerKwh : 0m;
            }

            totalAmount += obs.Quantity.Value * price;
            totalEnergy += obs.Quantity.Value;
        }

        var avgUnitPrice = totalEnergy != 0m ? totalAmount / totalEnergy : 0m;

        return SettlementLine.CreateSpot(
            settlementId,
            "Spotpris",
            EnergyQuantity.Create(totalEnergy),
            avgUnitPrice);
    }

    // --- Fixed electricity price (Fixed products only) ---

    /// <summary>
    /// Fixed electricity: total energy × fixed rate.
    /// The margin IS the full electricity price — no spot component.
    /// </summary>
    private static SettlementLine? CalculateFixedElectricityLine(
        Guid settlementId,
        TimeSeries timeSeries,
        SupplierMargin? activeMargin)
    {
        if (activeMargin is null) return null;

        return SettlementLine.CreateMargin(
            settlementId,
            "Elpris (fast)",
            timeSeries.TotalEnergy,
            activeMargin.PriceDkkPerKwh);
    }

    // --- Supplier margin addon (SpotAddon products only) ---

    /// <summary>
    /// Supplier margin addon: total energy × flat margin rate.
    /// Added on top of spot price for SpotAddon products.
    /// </summary>
    private static SettlementLine? CalculateMarginAddonLine(
        Guid settlementId,
        TimeSeries timeSeries,
        SupplierMargin? activeMargin)
    {
        if (activeMargin is null) return null;

        return SettlementLine.CreateMargin(
            settlementId,
            "Leverandørmargin",
            timeSeries.TotalEnergy,
            activeMargin.PriceDkkPerKwh);
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

    public PriceWithPoints(Price price)
    {
        Price = price;
        _sortedPoints = price.PricePoints
            .OrderBy(pp => pp.Timestamp)
            .Select(pp => (pp.Timestamp, pp.Price))
            .ToList();
    }

    public decimal? GetPriceAt(DateTimeOffset timestamp)
    {
        if (_sortedPoints.Count == 0) return null;

        if (Price.Type == PriceType.Abonnement)
            return _sortedPoints[0].Price;

        decimal? result = null;
        foreach (var point in _sortedPoints)
        {
            if (point.Timestamp > timestamp) break;
            result = point.Price;
        }

        return result;
    }

    public decimal? GetAveragePriceInHour(DateTimeOffset hourStart)
    {
        if (_sortedPoints.Count == 0) return null;

        var hourEnd = hourStart.AddHours(1);
        var pointsInHour = _sortedPoints
            .Where(p => p.Timestamp >= hourStart && p.Timestamp < hourEnd)
            .ToList();

        if (pointsInHour.Count == 0)
            return GetPriceAt(hourStart);

        return pointsInHour.Average(p => p.Price);
    }
}
