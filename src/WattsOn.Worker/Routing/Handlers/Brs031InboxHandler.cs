using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs031InboxHandler
{
    private readonly ILogger _logger;
    public Brs031InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var brs = message.BusinessProcess; // BRS-031 or BRS-037
        var businessReason = PayloadParser.GetString(payload, "businessReason") ?? message.DocumentType;

        switch (businessReason)
        {
            case "D18": // Price information / charge masterdata (create/update)
            {
                var chargeId = PayloadParser.GetString(payload, "chargeId")!;
                var ownerGln = GlnNumber.Create(PayloadParser.GetString(payload, "ownerGln")!);
                var priceTypeStr = PayloadParser.GetString(payload, "priceType")!;
                var description = PayloadParser.GetString(payload, "description") ?? "";
                var effectiveDate = DateTimeOffset.Parse(PayloadParser.GetString(payload, "effectiveDate")!);
                var stopDateStr = PayloadParser.GetString(payload, "stopDate");
                var stopDate = stopDateStr != null ? DateTimeOffset.Parse(stopDateStr) : (DateTimeOffset?)null;
                var resolutionStr = PayloadParser.GetString(payload, "resolution") ?? "PT1H";
                var vatExempt = payload.TryGetProperty("vatExempt", out var ve) && ve.GetBoolean();
                var isTax = payload.TryGetProperty("isTax", out var it) && it.GetBoolean();
                var isPassThrough = !payload.TryGetProperty("isPassThrough", out var ipt) || ipt.GetBoolean();

                var priceType = Enum.Parse<PriceType>(priceTypeStr, ignoreCase: true);
                var resolution = Enum.Parse<Resolution>(resolutionStr, ignoreCase: true);

                // Classify what settlement role this price plays
                var category = ClassifyPrice(payload, priceType, isTax, ownerGln.Value, message.SenderGln);

                // Find existing price by ChargeId + OwnerGln
                var existingPrice = await db.Prices
                    .Include(p => p.PricePoints)
                    .Where(p => p.ChargeId == chargeId)
                    .FirstOrDefaultAsync(p => p.OwnerGln.Value == ownerGln.Value, ct);

                var result = Brs031Handler.ProcessPriceInformation(
                    chargeId, ownerGln, priceType, description, effectiveDate, stopDate,
                    resolution, vatExempt, isTax, isPassThrough, existingPrice, category);

                if (result.IsNew)
                    db.Prices.Add(result.Price);

                _logger.LogInformation("{Brs} D18: {Action} price info {ChargeId} for {OwnerGln} (category={Category})",
                    brs, result.IsNew ? "Created" : "Updated", chargeId, ownerGln, category);
                break;
            }

            case "D08": // Price series / charge prices (add/replace points)
            {
                var chargeId = PayloadParser.GetString(payload, "chargeId")!;
                var ownerGln = GlnNumber.Create(PayloadParser.GetString(payload, "ownerGln")!);
                var startDate = DateTimeOffset.Parse(PayloadParser.GetString(payload, "startDate")!);
                var endDate = DateTimeOffset.Parse(PayloadParser.GetString(payload, "endDate")!);

                var price = await db.Prices
                    .Include(p => p.PricePoints)
                    .Where(p => p.ChargeId == chargeId)
                    .FirstOrDefaultAsync(p => p.OwnerGln.Value == ownerGln.Value, ct);

                if (price is null)
                {
                    _logger.LogWarning("{Brs} D08: Price not found for {ChargeId}/{OwnerGln}", brs, chargeId, ownerGln);
                    break;
                }

                var points = new List<(DateTimeOffset timestamp, decimal price)>();
                if (payload.TryGetProperty("points", out var pointsArray))
                {
                    foreach (var pt in pointsArray.EnumerateArray())
                    {
                        var timestamp = DateTimeOffset.Parse(pt.GetProperty("timestamp").GetString()!);
                        var amount = pt.GetProperty("price").GetDecimal();
                        points.Add((timestamp, amount));
                    }
                }

                var result = Brs031Handler.ProcessPriceSeries(price, startDate, endDate, points);

                _logger.LogInformation("{Brs} D08: Added {Count} price points to {ChargeId}", brs, result.PointsAdded, chargeId);
                break;
            }

            case "D17": // Price link update
            {
                var gsrnStr = PayloadParser.GetString(payload, "gsrn")!;
                var chargeId = PayloadParser.GetString(payload, "chargeId")!;
                var ownerGln = GlnNumber.Create(PayloadParser.GetString(payload, "ownerGln")!);
                var linkStart = DateTimeOffset.Parse(PayloadParser.GetString(payload, "linkStart")!);
                var linkEndStr = PayloadParser.GetString(payload, "linkEnd");
                var linkEnd = linkEndStr != null ? DateTimeOffset.Parse(linkEndStr) : (DateTimeOffset?)null;

                // Find metering point by GSRN
                var gsrn = Gsrn.Create(gsrnStr);
                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
                if (mp is null)
                {
                    _logger.LogWarning("{Brs} D17: Metering point not found for GSRN {Gsrn}", brs, gsrnStr);
                    break;
                }

                // Find price by ChargeId + OwnerGln
                var price = await db.Prices
                    .Where(p => p.ChargeId == chargeId)
                    .FirstOrDefaultAsync(p => p.OwnerGln.Value == ownerGln.Value, ct);

                if (price is null)
                {
                    _logger.LogWarning("{Brs} D17: Price not found for {ChargeId}/{OwnerGln}", brs, chargeId, ownerGln);
                    break;
                }

                // Find existing link
                var existingLink = await db.PriceLinks
                    .FirstOrDefaultAsync(pl => pl.PriceId == price.Id && pl.MeteringPointId == mp.Id, ct);

                var result = Brs031Handler.ProcessPriceLinkUpdate(
                    mp.Id, price.Id, linkStart, linkEnd, existingLink);

                if (result.IsNew)
                    db.PriceLinks.Add(result.Link);

                _logger.LogInformation("{Brs} D17: {Action} price link for GSRN {Gsrn} → {ChargeId}",
                    brs, result.IsNew ? "Created" : "Updated", gsrnStr, chargeId);
                break;
            }

            default:
                _logger.LogInformation("{Brs}: Unknown business reason '{Reason}' — skipping", brs, businessReason);
                break;
        }
    }

    /// <summary>
    /// Auto-classify a price's settlement category from available metadata.
    /// Uses explicit "category" field (if present in payload), then falls back to
    /// heuristics: IsTax → Elafgift, owner = recipient (supplier) → Leverandørtillæg,
    /// Abonnement from grid → NetAbonnement.
    /// For TSO tariffs (system/transmission/balance) that share the same owner GLN,
    /// classification relies on the payload carrying a "category" field or defaults to Andet.
    /// </summary>
    internal static PriceCategory ClassifyPrice(
        JsonElement payload, PriceType priceType, bool isTax,
        string ownerGln, string recipientGln)
    {
        // 1. Explicit category in payload (simulation or enriched messages)
        var explicit_ = PayloadParser.GetString(payload, "category");
        if (explicit_ != null && Enum.TryParse<PriceCategory>(explicit_, ignoreCase: true, out var parsed))
            return parsed;

        // 2. Tax tariff → Elafgift
        if (isTax && priceType == PriceType.Tarif)
            return PriceCategory.Elafgift;

        // 3. Owner matches recipient (the supplier themselves) → Andet
        //    (Supplier margin is a separate entity, not a DataHub charge.
        //     If a supplier sends a charge via DataHub, it's still categorised separately.)

        // 4. Subscription from a grid company → NetAbonnement
        if (priceType == PriceType.Abonnement)
            return PriceCategory.NetAbonnement;

        // 5. Can't distinguish system/transmission/balance/nettarif without more info
        return PriceCategory.Andet;
    }
}
