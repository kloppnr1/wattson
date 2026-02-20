using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Migration.Core.Models;
using WattsOn.Migration.XellentData.Entities;

namespace WattsOn.Migration.XellentData;

/// <summary>
/// Mirrors the CorrectionService logic from NewSettlement.XellentSettlement exactly.
/// Produces settlement provenance using the SAME queries and rate lookups.
/// This is the "reference" calculation — what XellentSettlement would produce.
/// </summary>
public class XellentSettlementService
{
    private readonly XellentDbContext _db;
    private readonly ILogger<XellentSettlementService> _logger;
    private const string DataAreaId = "hol";
    private const string DeliveryCategory = "El-ekstern";
    private const int TariffChargeTypeCode = 3;
    private static readonly DateTime NoEndDate = new(1900, 1, 1);
    private static readonly TimeZoneInfo DanishTz = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public XellentSettlementService(XellentDbContext db, ILogger<XellentSettlementService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Build settlement provenance for a metering point using CorrectionService-equivalent logic.
    /// </summary>
    public async Task<List<ExtractedSettlement>> BuildSettlementsAsync(string meteringPointId)
    {
        var result = new List<ExtractedSettlement>();

        // Step 1: Get all billing periods (same as CorrectionService.GetBillingPeriodsAsync)
        var periods = await _db.FlexBillingHistoryTables
            .Where(h => h.DataAreaId == DataAreaId
                     && h.MeteringPoint == meteringPointId
                     && h.DeliveryCategory == DeliveryCategory)
            .OrderBy(h => h.HistKeyNumber)
            .ToListAsync();

        _logger.LogInformation("Found {Count} billing periods for {Mp}", periods.Count, meteringPointId);

        foreach (var period in periods)
        {
            // Step 2: Get hourly data (same as CorrectionService.GetHourlyDataAsync)
            var hourlyLines = await _db.FlexBillingHistoryLines
                .Where(l => l.DataAreaId == DataAreaId && l.HistKeyNumber == period.HistKeyNumber)
                .OrderBy(l => l.DateTime24Hour)
                .ToListAsync();

            if (hourlyLines.Count == 0) continue;

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

            // Step 3: Get tariffs using EXACT CorrectionService.GetTariffsAsync logic
            var tariffLines = await GetTariffLinesWithProvenance(meteringPointId, periodStart, hourlyLines);

            // Step 4: Get product margin rates using CorrectionService.GetProductRatesForHour logic
            var productMarginLines = await GetProductMarginLinesWithProvenance(meteringPointId, periodStart, hourlyLines);

            var allTariffLines = tariffLines.Concat(productMarginLines).ToList();
            var totalAmount = electricityAmount + allTariffLines.Sum(t => t.AmountDkk);

            result.Add(new ExtractedSettlement
            {
                Gsrn = meteringPointId,
                PeriodStart = periodStart,
                PeriodEnd = period.ReqEndDate,
                BillingLogNum = period.BillingLogNum,
                HistKeyNumber = period.HistKeyNumber,
                TotalEnergyKwh = totalEnergy,
                ElectricityAmountDkk = electricityAmount,
                SpotAmountDkk = spotAmount,
                MarginAmountDkk = marginAmount,
                TariffLines = allTariffLines,
                TotalAmountDkk = totalAmount,
                HourlyLines = hourlyProvenance
            });
        }

        _logger.LogInformation("Built {Count} settlements for {Mp}", result.Count, meteringPointId);
        return result;
    }

    /// <summary>
    /// Exact mirror of CorrectionService.GetTariffsAsync + GetTariffRatesForHour
    /// </summary>
    private async Task<List<ExtractedTariffLine>> GetTariffLinesWithProvenance(
        string meteringPointId, DateTime forDate, List<FlexBillingHistoryLine> hourlyData)
    {
        var forDateOnly = forDate.Date;
        var tariffLines = new List<ExtractedTariffLine>();

        // All charge types — tariffs (code 3), subscriptions (code 2), fees (code 1)
        // For migration: capture everything that was invoiced, not just tariffs
        var tariffAssignments = await (
            from pec in _db.PriceElementChecks
            join pecd in _db.PriceElementCheckData
                on new { pec.DataAreaId, RefRecId = pec.RecId }
                equals new { pecd.DataAreaId, RefRecId = pecd.PriceElementCheckRefRecId }
            join pet in _db.PriceElementTables
                on new { pecd.DataAreaId, pecd.PartyChargeTypeId }
                equals new { pet.DataAreaId, pet.PartyChargeTypeId }
            where pec.DataAreaId == DataAreaId
               && pec.MeteringPointId == meteringPointId
               && pec.DeliveryCategory == DeliveryCategory
               && pecd.StartDate <= forDateOnly
               && (pecd.EndDate == NoEndDate || pecd.EndDate >= forDateOnly)
            group pet by pecd.PartyChargeTypeId into g
            select new { PartyChargeTypeId = g.Key, Description = g.First().Description, ChargeTypeCode = g.First().ChargeTypeCode }
        ).ToListAsync();

        foreach (var tariff in tariffAssignments)
        {
            // Same rate lookup as CorrectionService
            var candidateRates = await _db.PriceElementRates
                .Where(r => r.DataAreaId == DataAreaId
                         && r.PartyChargeTypeId == tariff.PartyChargeTypeId
                         && r.StartDate <= forDateOnly)
                .OrderByDescending(r => r.StartDate)
                .ToListAsync();

            if (candidateRates.Count == 0) continue;

            var rate = candidateRates[0];
            var isHourly = HasHourlyRates(rate);
            var isSubscription = tariff.ChargeTypeCode != TariffChargeTypeCode; // code 2 = subscription, code 1 = fee

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
                CandidateRateCount = candidateRates.Count,
                SelectionRule = $"ChargeTypeCode={tariff.ChargeTypeCode} ({chargeTypeLabel}): " +
                    $"most recent rate with StartDate <= {forDateOnly:yyyy-MM-dd} " +
                    $"(selected {rate.StartDate:yyyy-MM-dd} from {candidateRates.Count} candidates)"
            };

            if (isSubscription)
            {
                // Subscriptions/fees: flat monthly amount, NOT per-kWh
                // The rate from PriceElementRates IS the monthly amount (e.g. 29.17 DKK/month)
                var flatAmount = rate.Price;
                if (flatAmount == 0) continue;

                tariffLines.Add(new ExtractedTariffLine
                {
                    PartyChargeTypeId = tariff.PartyChargeTypeId,
                    Description = tariff.Description,
                    AmountDkk = flatAmount,
                    EnergyKwh = 0, // subscriptions aren't per-kWh
                    AvgUnitPrice = 0,
                    IsSubscription = true,
                    RateProvenance = provenance,
                    HourlyDetail = new() // no hourly breakdown for subscriptions
                });
            }
            else
            {
                // Tariffs: calculate per-hour amount = kWh × rate
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
    /// Mirror of CorrectionService.GetProductRatesForHour — product margin from ExuRateTable chain.
    /// This is the piece WattsOn extraction was MISSING.
    /// </summary>
    private async Task<List<ExtractedTariffLine>> GetProductMarginLinesWithProvenance(
        string meteringPointId, DateTime forDate, List<FlexBillingHistoryLine> hourlyData)
    {
        var forDateOnly = forDate.Date;
        var lines = new List<ExtractedTariffLine>();

        // Same chain as CorrectionService.GetProductRatesForHour:
        // Delpoint → Agreement → ContractPart → ProductExtend → InventTable → RateTable
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
               && dp.Meteringpoint == meteringPointId
               && dp.Deliverycategory == DeliveryCategory
               && cp.Startdate <= forDateOnly
               && (cp.Enddate >= forDateOnly || cp.Enddate == NoEndDate)
               && pe.Startdate <= forDateOnly
               && (pe.Enddate >= forDateOnly || pe.Enddate == NoEndDate)
               && inv.ItemType == 2
               && inv.ExuUseRateFromFlexPricing == 0
            select new { cp.Productnum, pe.Producttype }
        ).ToListAsync();

        foreach (var pt in productTypes)
        {
            var rate = await _db.ExuRateTables
                .Where(r => r.Dataareaid == DataAreaId
                         && r.Ratetype == pt.Producttype
                         && r.Deliverycategory == DeliveryCategory
                         && r.Startdate <= forDateOnly
                         && r.Productnum == "")
                .OrderByDescending(r => r.Startdate)
                .FirstOrDefaultAsync();

            if (rate == null || rate.Rate == 0) continue;

            var provenance = new TariffRateProvenance
            {
                Table = "EXU_RATETABLE",
                PartyChargeTypeId = pt.Producttype,
                RateStartDate = rate.Startdate,
                IsHourly = false,
                FlatRate = rate.Rate,
                CandidateRateCount = 1,
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
}
