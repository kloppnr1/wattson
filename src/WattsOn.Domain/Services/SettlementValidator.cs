using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Services;

/// <summary>
/// Validates that all required price components are present before settlement.
///
/// Three checks (conditional on PricingModel):
/// 1. DataHub charges — required categories must be linked (always)
/// 2. Spot prices — must cover every interval (SpotAddon only)
/// 3. Supplier margin — active rate must exist (always)
/// </summary>
public static class SettlementValidator
{
    /// <summary>
    /// Required DataHub charge categories for a valid settlement.
    /// </summary>
    private static readonly (PriceCategory Category, string Name)[] RequiredDataHubCategories =
    [
        (PriceCategory.Nettarif, "Nettarif"),
        (PriceCategory.Systemtarif, "Systemtarif"),
        (PriceCategory.Transmissionstarif, "Transmissionstarif"),
        (PriceCategory.Elafgift, "Elafgift"),
        (PriceCategory.Balancetarif, "Balancetarif"),
    ];

    /// <summary>
    /// Validate all price sources. Returns a combined list of issues (empty = all good).
    /// PricingModel determines whether spot prices are required.
    /// </summary>
    public static IReadOnlyList<string> Validate(
        IReadOnlyList<PriceWithPoints> datahubPrices,
        IReadOnlyList<SpotPrice> spotPrices,
        SupplierMargin? activeMargin,
        PricingModel pricingModel,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Resolution resolution)
    {
        var issues = new List<string>();

        // DataHub charges always required
        issues.AddRange(ValidateDataHubCategories(datahubPrices));
        issues.AddRange(ValidateDataHubCoverage(datahubPrices, periodStart, periodEnd));

        // Spot prices only required for SpotAddon products
        if (pricingModel == PricingModel.SpotAddon)
        {
            issues.AddRange(ValidateIntervalCoverage("Spotpris",
                spotPrices.Select(s => s.Timestamp).ToHashSet(),
                periodStart, periodEnd, resolution));
        }

        // Supplier margin always required (it's either the fixed price or the addon)
        if (activeMargin is null)
        {
            issues.Add("Leverandørmargin: ingen aktiv sats fundet for perioden");
        }

        return issues;
    }

    /// <summary>
    /// Validate that all required DataHub charge categories are present.
    /// </summary>
    public static IReadOnlyList<string> ValidateDataHubCategories(IReadOnlyList<PriceWithPoints> datahubPrices)
    {
        var missing = new List<string>();

        foreach (var (category, name) in RequiredDataHubCategories)
        {
            if (!datahubPrices.Any(p => p.Price.Category == category))
                missing.Add(name);
        }

        return missing;
    }

    /// <summary>
    /// Validate that DataHub prices have actual price points for the period.
    /// </summary>
    public static IReadOnlyList<string> ValidateDataHubCoverage(
        IReadOnlyList<PriceWithPoints> datahubPrices,
        DateTimeOffset periodStart,
        DateTimeOffset? periodEnd)
    {
        var incomplete = new List<string>();
        var end = periodEnd ?? periodStart.AddMonths(1);

        foreach (var priceWithPoints in datahubPrices)
        {
            var price = priceWithPoints.Price;

            if (price.Type == PriceType.Abonnement)
            {
                if (priceWithPoints.GetPriceAt(periodStart) is null)
                    incomplete.Add($"{price.Description} ({price.ChargeId}): ingen prispunkter");
                continue;
            }

            if (priceWithPoints.GetPriceAt(periodStart) is null)
                incomplete.Add($"{price.Description} ({price.ChargeId}): ingen prispunkter i perioden");
        }

        return incomplete;
    }

    /// <summary>
    /// Validate that a time-varying price source covers every interval in the settlement period.
    /// Spot prices must have a value for every hour (or 15-min) being settled.
    /// </summary>
    public static IReadOnlyList<string> ValidateIntervalCoverage(
        string sourceName,
        HashSet<DateTimeOffset> timestamps,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        Resolution resolution)
    {
        if (timestamps.Count == 0)
            return [$"{sourceName}: ingen priser fundet for perioden"];

        var interval = resolution switch
        {
            Resolution.PT15M => TimeSpan.FromMinutes(15),
            _ => TimeSpan.FromHours(1), // PT1H, P1D, P1M → check hourly
        };

        var missingCount = 0;
        var current = periodStart;
        while (current < periodEnd)
        {
            if (!timestamps.Contains(current))
                missingCount++;
            current = current.Add(interval);
        }

        if (missingCount > 0)
            return [$"{sourceName}: mangler priser for {missingCount} intervaller i perioden"];

        return [];
    }
}
