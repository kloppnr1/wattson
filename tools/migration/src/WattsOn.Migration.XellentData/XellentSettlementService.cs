using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Migration.Core.Models;
using WattsOn.Migration.XellentData.Entities;

namespace WattsOn.Migration.XellentData;

/// <summary>
/// Mirrors the CorrectionService logic from NewSettlement.XellentSettlement exactly.
/// Produces settlement provenance using the SAME queries and rate lookups.
/// This is the "reference" calculation — what XellentSettlement would produce.
///
/// OPTIMIZED: All reference data pre-loaded in ~6 queries, then iterated in memory.
/// Previous version did ~13 queries × 65 periods = ~845 queries (SIGKILL after timeout).
/// </summary>
public class XellentSettlementService
{
    private readonly XellentDbContext _db;
    private readonly ILogger<XellentSettlementService> _logger;
    private readonly string DataAreaId;
    private readonly string[] CompanyIds;
    private readonly string DeliveryCategory;
    private const int TariffChargeTypeCode = 3;
    private static readonly DateTime NoEndDate = new(1900, 1, 1);
    private static readonly TimeZoneInfo DanishTz = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public XellentSettlementService(XellentDbContext db, ILogger<XellentSettlementService> logger, XellentConfig config)
    {
        _db = db;
        _logger = logger;
        DataAreaId = config.DataAreaId;
        CompanyIds = config.CompanyIds;
        DeliveryCategory = config.DeliveryCategory;
    }

    /// <summary>
    /// Build settlement provenance for a metering point using CorrectionService-equivalent logic.
    /// Pre-loads all reference data in bulk, then iterates in memory.
    /// </summary>
    public async Task<List<ExtractedSettlement>> BuildSettlementsAsync(
        string gsrn, string? xellentMeteringPoint = null)
    {
        var result = new List<ExtractedSettlement>();

        // Search by GSRN first, fall back to Xellent METERINGPOINT column
        var searchIds = new List<string> { gsrn };
        if (!string.IsNullOrEmpty(xellentMeteringPoint) && xellentMeteringPoint != gsrn)
            searchIds.Add(xellentMeteringPoint);

        // ── BULK LOAD 1: All billing periods ──
        var periods = await _db.FlexBillingHistoryTables
            .Where(h => h.DataAreaId == DataAreaId
                     && searchIds.Contains(h.MeteringPoint)
                     && h.DeliveryCategory == DeliveryCategory)
            .OrderBy(h => h.HistKeyNumber)
            .ToListAsync();

        if (periods.Count == 0) return result;
        _logger.LogInformation("Found {Count} billing periods for {Mp} (searched: [{Ids}])",
            periods.Count, gsrn, string.Join(", ", searchIds));

        // ── BULK LOAD 2: ALL hourly lines for ALL periods at once ──
        var histKeys = periods.Select(p => p.HistKeyNumber).ToHashSet();
        var allHourlyLines = await _db.FlexBillingHistoryLines
            .Where(l => l.DataAreaId == DataAreaId && histKeys.Contains(l.HistKeyNumber))
            .OrderBy(l => l.HistKeyNumber).ThenBy(l => l.DateTime24Hour)
            .ToListAsync();

        var hourlyByPeriod = allHourlyLines
            .GroupBy(l => l.HistKeyNumber)
            .ToDictionary(g => g.Key, g => g.ToList());

        _logger.LogInformation("Loaded {Lines} hourly lines across {Periods} periods",
            allHourlyLines.Count, hourlyByPeriod.Count);

        // ── BULK LOAD 3: All tariff assignments for this metering point (no date filter — filter in memory) ──
        var tariffAssignments = await (
            from pec in _db.PriceElementChecks
            join pecd in _db.PriceElementCheckData
                on new { pec.DataAreaId, RefRecId = pec.RecId }
                equals new { pecd.DataAreaId, RefRecId = pecd.PriceElementCheckRefRecId }
            join pet in _db.PriceElementTables
                on new { pecd.DataAreaId, pecd.PartyChargeTypeId, pecd.ChargeTypeCode }
                equals new { pet.DataAreaId, pet.PartyChargeTypeId, pet.ChargeTypeCode }
            where pec.DataAreaId == DataAreaId
               && searchIds.Contains(pec.MeteringPointId)
               && pec.DeliveryCategory == DeliveryCategory
            select new TariffAssignment
            {
                PartyChargeTypeId = pecd.PartyChargeTypeId,
                ChargeTypeCode = pecd.ChargeTypeCode,
                Description = pet.Description,
                StartDate = pecd.StartDate,
                EndDate = pecd.EndDate,
                OwnerId = pecd.OwnerId
            }
        ).ToListAsync();

        _logger.LogInformation("Loaded {Count} tariff assignment rows", tariffAssignments.Count);

        // ── BULK LOAD 4: ALL price element rates for all relevant charge IDs ──
        // Filter by GridCompanyId (owner) and ValidInMarketToExcl (only active versions)
        var chargeKeys = tariffAssignments
            .Select(a => new { a.PartyChargeTypeId, a.ChargeTypeCode })
            .Distinct()
            .ToList();

        var chargeIds = chargeKeys.Select(k => k.PartyChargeTypeId).Distinct().ToList();

        // Get the active owner GLN per charge (most recent assignment)
        var ownerByCharge = tariffAssignments
            .GroupBy(a => (a.PartyChargeTypeId, a.ChargeTypeCode))
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.StartDate).First().OwnerId);
        var ownerIds = ownerByCharge.Values.Distinct().ToList();

        var allRates = await _db.PriceElementRates
            .Where(r => r.DataAreaId == DataAreaId
                && chargeIds.Contains(r.PartyChargeTypeId)
                && ownerIds.Contains(r.GridCompanyId)
                && r.ValidInMarketToExcl == NoEndDate)
            .OrderByDescending(r => r.StartDate)
            .ThenByDescending(r => r.RecId) // tie-breaker for duplicate StartDates
            .ToListAsync();

        // Group by (PartyChargeTypeId, ChargeTypeCode) for fast lookup
        // Also filter each group to only the correct owner, and dedup by StartDate
        var ratesByKey = allRates
            .GroupBy(r => (r.PartyChargeTypeId, r.ChargeTypeCode))
            .ToDictionary(g => g.Key, g =>
            {
                var ownerId = ownerByCharge.GetValueOrDefault(g.Key);
                return g
                    .Where(r => r.GridCompanyId == ownerId)
                    .GroupBy(r => r.StartDate)
                    .Select(dg => dg.First()) // Dedup: highest RecId wins (already sorted desc)
                    .OrderByDescending(r => r.StartDate)
                    .ThenByDescending(r => r.RecId)
                    .ToList();
            });

        _logger.LogInformation("Loaded {Count} rate rows for {Keys} charge keys (filtered by owner + active version)",
            ratesByKey.Values.Sum(v => v.Count), ratesByKey.Count);

        // ── BULK LOAD 5: Product type chain (Delpoint → Agreement → ContractPart → ProductExtend → InventTable) ──
        var productTypes = await (
            from dp in _db.ExuDelpoints
            join agr in _db.ExuAgreementTables
                on new { dp.Dataareaid, Agreementnum = dp.Attachmentnum }
                equals new { agr.Dataareaid, agr.Agreementnum }
            join cp in _db.ExuContractPartTables
                on new { agr.Dataareaid, Instagreenum = agr.Agreementnum }
                equals new { cp.Dataareaid, cp.Instagreenum }
            join pe in _db.ExuProductExtendTables
                on new { Dataareaid = cp.Dataareaid, cp.Productnum }
                equals new { Dataareaid = pe.DataAreaId, pe.Productnum }
            join inv in _db.InventTables
                on new { DataAreaId = pe.DataAreaId, ItemId = pe.Producttype }
                equals new { inv.DataAreaId, inv.ItemId }
            where dp.Dataareaid == DataAreaId
               && CompanyIds.Contains(dp.Companyid)
               && searchIds.Contains(dp.Meteringpoint)
               && dp.Deliverycategory == DeliveryCategory
               && inv.ItemType == 2
               && inv.ExuUseRateFromFlexPricing == 0
            select new ProductTypeInfo
            {
                Productnum = cp.Productnum,
                Producttype = pe.Producttype,
                CpStartDate = cp.Startdate,
                CpEndDate = cp.Enddate,
                PeStartDate = pe.Startdate,
                PeEndDate = pe.Enddate
            }
        ).ToListAsync();

        _logger.LogInformation("Loaded {Count} product type entries", productTypes.Count);

        // ── BULK LOAD 6: ALL rate table entries for relevant product types ──
        var productTypeNames = productTypes.Select(pt => pt.Producttype).Distinct().ToList();
        var allProductRates = productTypeNames.Count > 0
            ? await _db.ExuRateTables
                .Where(r => r.Dataareaid == DataAreaId
                         && CompanyIds.Contains(r.Companyid)
                         && productTypeNames.Contains(r.Ratetype)
                         && r.Deliverycategory == DeliveryCategory
                         && r.Productnum == "")
                .OrderByDescending(r => r.Startdate)
                .ThenByDescending(r => r.Recid) // tie-breaker for duplicate dates
                .ToListAsync()
            : new List<ExuRateTable>();

        var productRatesByType = allProductRates
            .GroupBy(r => r.Ratetype)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Startdate).ThenByDescending(r => r.Recid).ToList());

        _logger.LogInformation("Loaded {Count} product rate entries for {Types} types",
            allProductRates.Count, productRatesByType.Count);

        // ── BULK LOAD 7: Primary product periods (for naming the primary margin line) ──
        var primaryProducts = await (
            from dp in _db.ExuDelpoints
            join agr in _db.ExuAgreementTables
                on new { dp.Dataareaid, Agreementnum = dp.Attachmentnum }
                equals new { agr.Dataareaid, agr.Agreementnum }
            join cp in _db.ExuContractPartTables
                on new { agr.Dataareaid, Instagreenum = agr.Agreementnum }
                equals new { cp.Dataareaid, cp.Instagreenum }
            where dp.Dataareaid == DataAreaId
               && CompanyIds.Contains(dp.Companyid)
               && searchIds.Contains(dp.Meteringpoint)
               && dp.Deliverycategory == DeliveryCategory
               && cp.Productnum != ""
            select new PrimaryProductInfo
            {
                Productnum = cp.Productnum,
                CpStartDate = cp.Startdate,
                CpEndDate = cp.Enddate
            }
        ).Distinct().ToListAsync();

        _logger.LogInformation("Loaded {Count} primary product period entries", primaryProducts.Count);

        _logger.LogInformation("All reference data loaded — building {Count} settlements in memory", periods.Count);

        // ═══════════════════════════════════════════════════════════
        // IN-MEMORY: Iterate periods using pre-loaded data
        // ═══════════════════════════════════════════════════════════
        foreach (var period in periods)
        {
            if (!hourlyByPeriod.TryGetValue(period.HistKeyNumber, out var hourlyLines) || hourlyLines.Count == 0)
                continue;

            var periodStart = period.ReqStartDate != NoEndDate && period.ReqStartDate.Year > 1901
                ? period.ReqStartDate
                : hourlyLines.Min(l => l.DateTime24Hour);

            // Build electricity hourly provenance
            var hourlyProvenance = hourlyLines.Select(l => new HourlyLine
            {
                Timestamp = ToUtc(l.DateTime24Hour),
                Kwh = l.TimeValue,
                SpotPriceDkkPerKwh = l.PowerExchangePrice,
                CalculatedPriceDkkPerKwh = l.CalculatedPrice,
                SpotAmountDkk = l.TimeValue * l.PowerExchangePrice,
                MarginAmountDkk = l.TimeValue * (l.CalculatedPrice - l.PowerExchangePrice),
                ElectricityAmountDkk = l.TimeValue * l.CalculatedPrice
            }).ToList();

            var totalEnergy = hourlyLines.Sum(l => l.TimeValue);
            var electricityAmount = hourlyLines.Sum(l => l.TimeValue * l.CalculatedPrice);
            var spotAmount = hourlyLines.Sum(l => l.TimeValue * l.PowerExchangePrice);
            var marginAmount = electricityAmount - spotAmount;

            // Tariff lines — in-memory filtering of pre-loaded data
            var tariffLines = BuildTariffLines(periodStart, hourlyLines, tariffAssignments, ratesByKey);

            // Addon product margin lines — in-memory filtering of pre-loaded data
            var addonMarginLines = BuildProductMarginLines(periodStart, hourlyLines, productTypes, productRatesByType);

            // Primary product margin line — residual from FlexBillingHistory
            // (total margin minus addon amounts = primary product's share)
            var primaryLine = BuildPrimaryMarginLine(periodStart, marginAmount, totalEnergy, addonMarginLines, primaryProducts);
            var productMarginLines = primaryLine != null
                ? new[] { primaryLine }.Concat(addonMarginLines).ToList()
                : addonMarginLines;

            var allTariffLines = tariffLines.Concat(productMarginLines).ToList();
            var totalAmount = electricityAmount + allTariffLines.Sum(t => t.AmountDkk);

            result.Add(new ExtractedSettlement
            {
                Gsrn = gsrn,
                PeriodStart = periodStart,
                PeriodEnd = period.ReqEndDate,
                BillingLogNum = period.BillingLogNum,
                HistKeyNumber = period.HistKeyNumber,
                TotalEnergyKwh = totalEnergy,
                ElectricityAmountDkk = electricityAmount,
                SpotAmountDkk = spotAmount,
                MarginAmountDkk = 0, // all margin is in per-product PRODUCT: lines
                TariffLines = allTariffLines,
                TotalAmountDkk = totalAmount,
                HourlyLines = hourlyProvenance
            });
        }

        _logger.LogInformation("Built {Count} settlements for {Mp}", result.Count, gsrn);
        return result;
    }

    /// <summary>
    /// In-memory equivalent of GetTariffLinesWithProvenance.
    /// Uses pre-loaded tariff assignments and rates.
    /// </summary>
    private List<ExtractedTariffLine> BuildTariffLines(
        DateTime forDate,
        List<FlexBillingHistoryLine> hourlyData,
        List<TariffAssignment> allAssignments,
        Dictionary<(string, int), List<PriceElementRates>> ratesByKey)
    {
        var forDateOnly = forDate.Date;
        var tariffLines = new List<ExtractedTariffLine>();

        // Filter assignments valid for this date, then deduplicate by (PartyChargeTypeId, ChargeTypeCode)
        var activeAssignments = allAssignments
            .Where(a => a.StartDate <= forDateOnly && (a.EndDate == NoEndDate || a.EndDate >= forDateOnly))
            .GroupBy(a => (a.PartyChargeTypeId, a.ChargeTypeCode))
            .Select(g => g.First())
            .ToList();

        foreach (var tariff in activeAssignments)
        {
            var key = (tariff.PartyChargeTypeId, tariff.ChargeTypeCode);
            if (!ratesByKey.TryGetValue(key, out var candidateRates)) continue;

            // Find most recent rate with StartDate <= forDate (rates are pre-sorted desc)
            var rate = candidateRates.FirstOrDefault(r => r.StartDate <= forDateOnly);
            if (rate == null) continue;

            var applicableCandidates = candidateRates.Count(r => r.StartDate <= forDateOnly);
            var isHourly = HasHourlyRates(rate);
            var isSubscription = tariff.ChargeTypeCode != TariffChargeTypeCode;

            var hourlyRates = isHourly ? new decimal[24] : null;
            if (isHourly)
                for (int h = 1; h <= 24; h++)
                    hourlyRates![h - 1] = rate.GetPriceForHour(h);

            var chargeTypeLabel = tariff.ChargeTypeCode switch
            {
                3 => "tariff",
                2 => "subscription",
                1 => "fee",
                _ => $"type-{tariff.ChargeTypeCode}"
            };

            var provenance = new TariffRateProvenance
            {
                Table = "EXU_PRICEELEMENTRATES",
                PartyChargeTypeId = tariff.PartyChargeTypeId,
                RateStartDate = rate.StartDate,
                IsHourly = isHourly,
                FlatRate = rate.Price,
                HourlyRates = hourlyRates,
                CandidateRateCount = applicableCandidates,
                SelectionRule = $"ChargeTypeCode={tariff.ChargeTypeCode} ({chargeTypeLabel}): " +
                    $"most recent rate with StartDate <= {forDateOnly:yyyy-MM-dd} " +
                    $"(selected {rate.StartDate:yyyy-MM-dd} from {applicableCandidates} candidates)"
            };

            if (isSubscription)
            {
                var flatAmount = rate.Price;
                if (flatAmount == 0) continue;

                tariffLines.Add(new ExtractedTariffLine
                {
                    PartyChargeTypeId = tariff.PartyChargeTypeId,
                    Description = tariff.Description,
                    AmountDkk = flatAmount,
                    EnergyKwh = 0,
                    AvgUnitPrice = 0,
                    IsSubscription = true,
                    RateProvenance = provenance,
                    HourlyDetail = new()
                });
            }
            else
            {
                decimal totalAmount = 0m;
                decimal totalEnergy = 0m;
                var hourlyDetail = new List<HourlyTariffDetail>();

                foreach (var line in hourlyData)
                {
                    var hourNum = line.DateTime24Hour.Hour + 1;
                    var hourlyRate = rate.GetPriceForHour(hourNum);
                    var effectiveRate = hourlyRate > 0 ? hourlyRate : rate.Price;

                    if (effectiveRate <= 0) continue;

                    var lineAmount = line.TimeValue * effectiveRate;
                    totalAmount += lineAmount;
                    totalEnergy += line.TimeValue;

                    hourlyDetail.Add(new HourlyTariffDetail
                    {
                        Timestamp = ToUtc(line.DateTime24Hour),
                        Hour = hourNum,
                        Kwh = line.TimeValue,
                        RateDkkPerKwh = effectiveRate,
                        AmountDkk = lineAmount
                    });
                }

                if (totalAmount == 0) continue;

                tariffLines.Add(new ExtractedTariffLine
                {
                    PartyChargeTypeId = tariff.PartyChargeTypeId,
                    Description = tariff.Description,
                    AmountDkk = totalAmount,
                    EnergyKwh = totalEnergy,
                    AvgUnitPrice = totalEnergy != 0 ? totalAmount / totalEnergy : 0,
                    IsSubscription = false,
                    RateProvenance = provenance,
                    HourlyDetail = hourlyDetail
                });
            }
        }

        return tariffLines;
    }

    /// <summary>
    /// In-memory equivalent of GetProductMarginLinesWithProvenance.
    /// Uses pre-loaded product types and rate tables.
    /// </summary>
    private List<ExtractedTariffLine> BuildProductMarginLines(
        DateTime forDate,
        List<FlexBillingHistoryLine> hourlyData,
        List<ProductTypeInfo> allProductTypes,
        Dictionary<string, List<ExuRateTable>> productRatesByType)
    {
        var forDateOnly = forDate.Date;
        var lines = new List<ExtractedTariffLine>();

        // Filter product types valid for this date
        var activeProducts = allProductTypes
            .Where(pt => pt.CpStartDate <= forDateOnly
                      && (pt.CpEndDate >= forDateOnly || pt.CpEndDate == NoEndDate)
                      && pt.PeStartDate <= forDateOnly
                      && (pt.PeEndDate >= forDateOnly || pt.PeEndDate == NoEndDate))
            .ToList();

        foreach (var pt in activeProducts)
        {
            if (!productRatesByType.TryGetValue(pt.Producttype, out var rates)) continue;

            // Find most recent rate with StartDate <= forDate (pre-sorted desc)
            var rate = rates.FirstOrDefault(r => r.Startdate <= forDateOnly);
            if (rate == null || rate.Rate == 0) continue;

            var provenance = new TariffRateProvenance
            {
                Table = "EXU_RATETABLE",
                PartyChargeTypeId = pt.Producttype,
                RateStartDate = rate.Startdate,
                IsHourly = false,
                FlatRate = rate.Rate,
                CandidateRateCount = rates.Count(r => r.Startdate <= forDateOnly),
                SelectionRule = $"CorrectionService.GetProductRatesForHour: " +
                    $"Delpoint→Agreement→ContractPart(product={pt.Productnum})→ProductExtend(type={pt.Producttype})→" +
                    $"InventTable(ItemType=2, UseRateFromFlexPricing=0)→RateTable(generic, Productnum='')"
            };

            decimal totalAmount = 0m;
            decimal totalEnergy = 0m;
            var hourlyDetail = new List<HourlyTariffDetail>();

            foreach (var line in hourlyData)
            {
                var lineAmount = line.TimeValue * rate.Rate;
                totalAmount += lineAmount;
                totalEnergy += line.TimeValue;

                hourlyDetail.Add(new HourlyTariffDetail
                {
                    Timestamp = ToUtc(line.DateTime24Hour),
                    Hour = line.DateTime24Hour.Hour + 1,
                    Kwh = line.TimeValue,
                    RateDkkPerKwh = rate.Rate,
                    AmountDkk = lineAmount
                });
            }

            lines.Add(new ExtractedTariffLine
            {
                PartyChargeTypeId = $"PRODUCT:{pt.Producttype}",
                Description = $"Product Margin ({pt.Producttype}) [product={pt.Productnum}]",
                AmountDkk = totalAmount,
                EnergyKwh = totalEnergy,
                AvgUnitPrice = totalEnergy != 0 ? totalAmount / totalEnergy : 0,
                RateProvenance = provenance,
                HourlyDetail = hourlyDetail
            });
        }

        return lines;
    }

    /// <summary>
    /// Build a PRODUCT: margin line for the primary product.
    /// Amount = FlexBillingHistory total margin minus addon product margin amounts.
    /// This gives the primary product's share of the margin using ground-truth billing data.
    /// </summary>
    private ExtractedTariffLine? BuildPrimaryMarginLine(
        DateTime forDate,
        decimal totalMarginAmount,
        decimal totalEnergy,
        List<ExtractedTariffLine> addonLines,
        List<PrimaryProductInfo> primaryProducts)
    {
        var forDateOnly = forDate.Date;

        // Find active primary product by contract dates
        var activePrimary = primaryProducts
            .FirstOrDefault(pp => pp.CpStartDate <= forDateOnly
                && (pp.CpEndDate >= forDateOnly || pp.CpEndDate == NoEndDate));

        if (activePrimary == null) return null;

        var addonTotal = addonLines.Sum(l => l.AmountDkk);
        var primaryAmount = totalMarginAmount - addonTotal;

        // Skip if no margin at all
        if (primaryAmount == 0 && totalMarginAmount == 0) return null;

        var avgRate = totalEnergy != 0 ? primaryAmount / totalEnergy : 0;

        return new ExtractedTariffLine
        {
            PartyChargeTypeId = $"PRODUCT:{activePrimary.Productnum}",
            Description = $"Product Margin ({activePrimary.Productnum}) [primary]",
            AmountDkk = primaryAmount,
            EnergyKwh = totalEnergy,
            AvgUnitPrice = avgRate,
            RateProvenance = new TariffRateProvenance
            {
                Table = "FlexBillingHistoryLine (residual)",
                PartyChargeTypeId = activePrimary.Productnum,
                RateStartDate = forDate,
                IsHourly = false,
                FlatRate = avgRate,
                CandidateRateCount = 0,
                SelectionRule = $"Primary product margin = FlexBillingHistory margin ({totalMarginAmount:F4}) - addon margins ({addonTotal:F4})"
            }
        };
    }

    private static bool HasHourlyRates(PriceElementRates rate)
    {
        return rate.Price2 != 0 || rate.Price3 != 0 || rate.Price4 != 0 ||
               rate.Price5 != 0 || rate.Price6 != 0 || rate.Price7 != 0 ||
               rate.Price8 != 0 || rate.Price9 != 0 || rate.Price10 != 0 ||
               rate.Price11 != 0 || rate.Price12 != 0 || rate.Price13 != 0 ||
               rate.Price14 != 0 || rate.Price15 != 0 || rate.Price16 != 0 ||
               rate.Price17 != 0 || rate.Price18 != 0 || rate.Price19 != 0 ||
               rate.Price20 != 0 || rate.Price21 != 0 || rate.Price22 != 0 ||
               rate.Price23 != 0 || rate.Price24 != 0;
    }

    private static DateTimeOffset ToUtc(DateTime localDate)
    {
        if (localDate.Kind == DateTimeKind.Utc) return localDate;
        var offset = DanishTz.GetUtcOffset(localDate);
        return new DateTimeOffset(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), offset).ToUniversalTime();
    }

    // ── Helper records for pre-loaded data ──

    private record TariffAssignment
    {
        public string PartyChargeTypeId { get; init; } = null!;
        public int ChargeTypeCode { get; init; }
        public string Description { get; init; } = null!;
        public DateTime StartDate { get; init; }
        public DateTime EndDate { get; init; }
        public decimal OwnerId { get; init; }
    }

    private record ProductTypeInfo
    {
        public string Productnum { get; init; } = null!;
        public string Producttype { get; init; } = null!;
        public DateTime CpStartDate { get; init; }
        public DateTime CpEndDate { get; init; }
        public DateTime PeStartDate { get; init; }
        public DateTime PeEndDate { get; init; }
    }

    private record PrimaryProductInfo
    {
        public string Productnum { get; init; } = null!;
        public DateTime CpStartDate { get; init; }
        public DateTime CpEndDate { get; init; }
    }
}
