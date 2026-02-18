using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles BRS-031 (Price/Charge updates) from DataHub.
/// Processes three message types (per RSM-033/RSM-034 specs):
///   D08 — Price series / Update Charge Prices (actual price amounts per hour/day/month)
///   D18 — Price information / Update Charge Information (metadata: name, type, VAT, tax, etc.)
///   D17 — Price link updates (link/unlink price to metering point)
/// Pure domain logic — no persistence, returns results for the caller to execute.
/// </summary>
public static class Brs031Handler
{
    // --- Result records ---

    public record PriceInformationResult(Price Price, bool IsNew);

    public record PriceSeriesResult(Price Price, int PointsAdded);

    public record PriceLinkResult(PriceLink Link, bool IsNew);

    // --- D18: Process Price Information (Charge Masterdata) ---

    /// <summary>
    /// Create or update a price from a D18 (Update Charge Information) message.
    /// D18 carries metadata: description, type, VAT group, tax flag, pass-through flag.
    /// If an existing price with the same ChargeId + OwnerGln is provided, it's updated.
    /// Otherwise a new price is created.
    /// </summary>
    public static PriceInformationResult ProcessPriceInformation(
        string chargeId,
        GlnNumber ownerGln,
        PriceType priceType,
        string description,
        DateTimeOffset effectiveDate,
        DateTimeOffset? stopDate,
        Resolution resolution,
        bool vatExempt,
        bool isTax,
        bool isPassThrough,
        Price? existingPrice)
    {
        var validityPeriod = stopDate.HasValue
            ? Period.Create(effectiveDate, stopDate.Value)
            : Period.From(effectiveDate);

        if (existingPrice is not null)
        {
            // Update existing price
            existingPrice.UpdateValidity(validityPeriod);
            existingPrice.UpdatePriceInfo(description, isTax, isPassThrough);
            existingPrice.UpdateVatExempt(vatExempt);
            return new PriceInformationResult(existingPrice, false);
        }

        // Create new price
        var price = Price.Create(
            chargeId,
            ownerGln,
            priceType,
            description,
            validityPeriod,
            vatExempt,
            resolution,
            isTax,
            isPassThrough);

        return new PriceInformationResult(price, true);
    }

    // --- D08: Process Price Series (Charge Prices) ---

    /// <summary>
    /// Add or replace price points on an existing price from a D08 (Update Charge Prices) message.
    /// D08 carries the actual price amounts per hour/day/month.
    /// Clears existing points in the given date range and adds the new ones.
    /// </summary>
    public static PriceSeriesResult ProcessPriceSeries(
        Price price,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        IEnumerable<(DateTimeOffset timestamp, decimal price)> points)
    {
        if (price is null)
            throw new ArgumentNullException(nameof(price));

        var pointsAdded = price.ReplacePricePoints(startDate, endDate, points);

        return new PriceSeriesResult(price, pointsAdded);
    }

    // --- D17: Process Price Link Update ---

    /// <summary>
    /// Create or update a price link from a D17 (ProcessPriceLinkUpdate) message.
    /// If an existing link for the same price+metering point is provided, its period is updated.
    /// Otherwise a new link is created.
    /// </summary>
    public static PriceLinkResult ProcessPriceLinkUpdate(
        Guid meteringPointId,
        Guid priceId,
        DateTimeOffset linkStart,
        DateTimeOffset? linkEnd,
        PriceLink? existingLink)
    {
        var linkPeriod = linkEnd.HasValue
            ? Period.Create(linkStart, linkEnd.Value)
            : Period.From(linkStart);

        if (existingLink is not null)
        {
            // Update existing link period
            existingLink.UpdatePeriod(linkPeriod);
            return new PriceLinkResult(existingLink, false);
        }

        // Create new link
        var link = PriceLink.Create(meteringPointId, priceId, linkPeriod);
        return new PriceLinkResult(link, true);
    }
}
