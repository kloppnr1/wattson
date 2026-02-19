using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;

namespace WattsOn.Domain.Services;

/// <summary>
/// Validates that all required price elements are linked to a metering point
/// before settlement calculation. A settlement missing mandatory price elements
/// would produce incorrect amounts for invoicing.
///
/// Required price elements for a Danish consumer metering point:
/// 1. Spotpris (SPOT-*) — wholesale electricity cost
/// 2. Nettarif (NET-T-*) — grid company distribution tariff
/// 3. Systemtarif (SYS-T-*) — TSO system tariff
/// 4. Transmissionstarif (TRANS-T-*) — TSO transmission tariff
/// 5. Elafgift (ELAFG-*) — electricity tax
/// 6. Balancetarif (BAL-T-*) — TSO balance tariff
///
/// Supplier margin (MARGIN-*) is optional — some suppliers include it,
/// others handle it outside the settlement engine.
/// </summary>
public static class SettlementValidator
{
    /// <summary>
    /// Required charge ID prefixes. A metering point must have at least one
    /// linked price matching each prefix for a settlement to be valid.
    /// </summary>
    private static readonly (string Prefix, string Name)[] RequiredPriceElements =
    [
        ("SPOT-", "Spotpris"),
        ("NET-T-", "Nettarif"),
        ("SYS-T-", "Systemtarif"),
        ("TRANS-T-", "Transmissionstarif"),
        ("ELAFG-", "Elafgift"),
        ("BAL-T-", "Balancetarif"),
    ];

    /// <summary>
    /// Validate that all required price elements are present in the linked prices.
    /// Returns a list of missing element names (empty = all good).
    /// </summary>
    public static IReadOnlyList<string> ValidatePriceCompleteness(IReadOnlyList<PriceWithPoints> activePrices)
    {
        var missing = new List<string>();

        foreach (var (prefix, name) in RequiredPriceElements)
        {
            var hasElement = activePrices.Any(p => p.Price.ChargeId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
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
