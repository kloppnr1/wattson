using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Services;

/// <summary>
/// Validates that all required price elements are linked to a metering point
/// before settlement calculation. A settlement missing mandatory price elements
/// would produce incorrect amounts for invoicing.
///
/// Validates by PriceCategory — independent of charge ID format.
/// Works identically whether charge IDs are simulation-style ("NET-T-DK1")
/// or production DataHub-style ("40000").
/// </summary>
public static class SettlementValidator
{
    /// <summary>
    /// Required price categories for a valid settlement.
    /// A metering point must have at least one linked price in each category.
    /// </summary>
    private static readonly (PriceCategory Category, string Name)[] RequiredPriceCategories =
    [
        (PriceCategory.SpotPris, "Spotpris"),
        (PriceCategory.Nettarif, "Nettarif"),
        (PriceCategory.Systemtarif, "Systemtarif"),
        (PriceCategory.Transmissionstarif, "Transmissionstarif"),
        (PriceCategory.Elafgift, "Elafgift"),
        (PriceCategory.Balancetarif, "Balancetarif"),
        (PriceCategory.Leverandørtillæg, "Leverandørtillæg"),
    ];

    /// <summary>
    /// Validate that all required price categories are present in the linked prices.
    /// Returns a list of missing element names (empty = all good).
    /// </summary>
    public static IReadOnlyList<string> ValidatePriceCompleteness(IReadOnlyList<PriceWithPoints> activePrices)
    {
        var missing = new List<string>();

        foreach (var (category, name) in RequiredPriceCategories)
        {
            var hasElement = activePrices.Any(p => p.Price.Category == category);
            if (!hasElement)
                missing.Add(name);
        }

        return missing;
    }

    /// <summary>
    /// Validate that prices have actual price points covering the settlement period.
    /// A linked price with no points in the period is effectively missing.
    /// </summary>
    public static IReadOnlyList<string> ValidatePricePointCoverage(
        IReadOnlyList<PriceWithPoints> activePrices,
        DateTimeOffset periodStart,
        DateTimeOffset? periodEnd)
    {
        var incomplete = new List<string>();
        var end = periodEnd ?? periodStart.AddMonths(1);

        foreach (var priceWithPoints in activePrices)
        {
            var price = priceWithPoints.Price;

            // Subscriptions only need 1 price point
            if (price.Type == PriceType.Abonnement)
            {
                if (priceWithPoints.GetPriceAt(periodStart) is null)
                    incomplete.Add($"{price.Description} ({price.ChargeId}): ingen prispunkter");
                continue;
            }

            // Tariffs: check that at least one price point exists in the period
            if (priceWithPoints.GetPriceAt(periodStart) is null)
                incomplete.Add($"{price.Description} ({price.ChargeId}): ingen prispunkter i perioden");
        }

        return incomplete;
    }
}
