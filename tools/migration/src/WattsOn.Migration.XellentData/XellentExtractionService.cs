using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Migration.Core.Models;
using WattsOn.Migration.XellentData.Entities;

namespace WattsOn.Migration.XellentData;

/// <summary>
/// Extracts customer, product, and time series data from Xellent for WattsOn migration.
/// Simplified version of the existing DataExtractionService — no profiles, no waves.
/// Takes account numbers directly.
/// </summary>
public class XellentExtractionService
{
    private readonly XellentDbContext _db;
    private readonly ILogger<XellentExtractionService> _logger;
    private const string DataAreaId = "hol";
    private const string DeliveryCategory = "El-ekstern";
    private static readonly DateTime NoEndDate = new(1900, 1, 1);
    private static readonly TimeZoneInfo DanishTz = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public XellentExtractionService(XellentDbContext db, ILogger<XellentExtractionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Extract customers with their metering points, supplies, and product history.
    /// </summary>
    public async Task<List<ExtractedCustomer>> ExtractCustomersAsync(string[] accountNumbers)
    {
        var today = DateTime.Now.Date;

        // Join: CustTable → Contract → ContractPart → Agreement → Delpoint
        var data = await (
            from cust in _db.CustTables
            join contract in _db.ExuContractTables
                on new { cust.Accountnum, cust.Dataareaid }
                equals new { Accountnum = contract.Custaccount, contract.Dataareaid }
            join contractPart in _db.ExuContractPartTables
                on new { contract.Contractnum, contract.Dataareaid }
                equals new { contractPart.Contractnum, contractPart.Dataareaid }
            join agreement in _db.ExuAgreementTables
                on new { Agreementnum = contractPart.Instagreenum, contractPart.Dataareaid, contractPart.Companyid }
                equals new { agreement.Agreementnum, agreement.Dataareaid, agreement.Companyid }
            join delpoint in _db.ExuDelpoints
                on new { Attachmentnum = agreement.Agreementnum, agreement.Dataareaid, agreement.Companyid }
                equals new { Attachmentnum = delpoint.Attachmentnum, delpoint.Dataareaid, delpoint.Companyid }
            where cust.Dataareaid == DataAreaId
                && accountNumbers.Contains(cust.Accountnum)
                && contract.Deliverycategory == DeliveryCategory
                && delpoint.Deliverycategory == DeliveryCategory
            select new
            {
                Customer = cust,
                Contract = contract,
                ContractPart = contractPart,
                Delpoint = delpoint
            }
        ).ToListAsync();

        // Group by customer
        var customers = data
            .GroupBy(d => d.Customer.Accountnum)
            .Select(g =>
            {
                var first = g.First();
                var customer = new ExtractedCustomer
                {
                    AccountNumber = first.Customer.Accountnum,
                    Name = first.Customer.Name,
                    Cpr = NullIfEmpty(first.Customer.ExuCprDisponent1),
                    Cvr = NullIfEmpty(first.Customer.ExuCvrDisponent1),
                    Email = NullIfEmpty(first.Customer.Email),
                    Phone = NullIfEmpty(first.Customer.Phone) ?? NullIfEmpty(first.Customer.Cellularphone),
                };

                // Group metering points — use GSRN if available, fall back to METERINGPOINT
                var mps = g
                    .GroupBy(d => GetGsrn(d.Delpoint))
                    .Where(mg => !string.IsNullOrEmpty(mg.Key))
                    .Select(mg =>
                    {
                        var firstMp = mg.First();
                        var mp = new ExtractedMeteringPoint
                        {
                            Gsrn = GetGsrn(firstMp.Delpoint),
                            GridArea = MapGridArea(firstMp.Delpoint.Powerexchangearea),
                            SupplyStart = ToUtc(firstMp.Contract.Contractstartdate),
                            SupplyEnd = firstMp.Contract.Contractenddate == NoEndDate
                                ? null
                                : ToUtc(firstMp.Contract.Contractenddate),
                        };

                        // Product periods from contract parts
                        mp.ProductPeriods = mg
                            .Where(d => !string.IsNullOrEmpty(d.ContractPart.Productnum))
                            .Select(d => new ExtractedProductPeriod
                            {
                                ProductName = d.ContractPart.Productnum,
                                Start = ToUtc(d.ContractPart.Startdate),
                                End = d.ContractPart.Enddate == NoEndDate ? null : ToUtc(d.ContractPart.Enddate),
                            })
                            .DistinctBy(pp => (pp.ProductName, pp.Start))
                            .OrderBy(pp => pp.Start)
                            .ToList();

                        return mp;
                    })
                    .ToList();

                customer.MeteringPoints = mps;
                return customer;
            })
            .ToList();

        return customers;
    }

    /// <summary>
    /// Extract distinct products and their rate history from all account numbers.
    /// </summary>
    public async Task<List<ExtractedProduct>> ExtractDistinctProductsAsync(string[] accountNumbers)
    {
        // Get distinct product numbers from contract parts
        var productNums = await (
            from cust in _db.CustTables
            join contract in _db.ExuContractTables
                on new { cust.Accountnum, cust.Dataareaid }
                equals new { Accountnum = contract.Custaccount, contract.Dataareaid }
            join contractPart in _db.ExuContractPartTables
                on new { contract.Contractnum, contract.Dataareaid }
                equals new { contractPart.Contractnum, contractPart.Dataareaid }
            where cust.Dataareaid == DataAreaId
                && accountNumbers.Contains(cust.Accountnum)
                && contract.Deliverycategory == DeliveryCategory
                && contractPart.Productnum != ""
            select contractPart.Productnum
        ).Distinct().ToListAsync();

        var products = new List<ExtractedProduct>();

        foreach (var productNum in productNums)
        {
            // Detect pricing model from InventTable
            // ExuUseRateFromFlexPricing == 1 → SpotAddon (spot + margin addon)
            // ExuUseRateFromFlexPricing == 0 → Fixed (margin IS the full electricity price)
            var inventItem = await _db.InventTables
                .FirstOrDefaultAsync(i => i.DataAreaId == DataAreaId && i.ItemId == productNum);
            var isSpot = inventItem?.ExuUseRateFromFlexPricing == 1;
            var pricingModel = isSpot ? "SpotAddon" : "Fixed";

            // Get product-specific rate type from ExuProductExtendTable
            var productExtend = await _db.ExuProductExtendTables
                .Where(pe => pe.DataAreaId == DataAreaId && pe.Productnum == productNum)
                .OrderByDescending(pe => pe.Startdate)
                .FirstOrDefaultAsync();

            List<ExuRateTable> rates;
            if (productExtend != null)
            {
                // Get rates matching this product's rate type
                rates = await _db.ExuRateTables
                    .Where(r => r.Dataareaid == DataAreaId
                        && r.Deliverycategory == DeliveryCategory
                        && r.Ratetype == productExtend.Producttype
                        && r.Productnum == "" // Generic rates (per-kWh), not subscriptions
                        && r.Rate != 0)
                    .OrderBy(r => r.Startdate)
                    .ToListAsync();
            }
            else
            {
                // Fallback: get all generic rates
                rates = await _db.ExuRateTables
                    .Where(r => r.Dataareaid == DataAreaId
                        && r.Deliverycategory == DeliveryCategory
                        && r.Productnum == ""
                        && r.Rate != 0)
                    .OrderBy(r => r.Startdate)
                    .ToListAsync();
            }

            products.Add(new ExtractedProduct
            {
                Name = productNum,
                PricingModel = pricingModel,
                Rates = rates.Select(r => new ExtractedRate
                {
                    StartDate = ToUtc(r.Startdate),
                    RateDkkPerKwh = r.Rate,
                }).ToList()
            });

            _logger.LogInformation("Product {Name}: {Model}, {RateType}, {RateCount} rates",
                productNum, pricingModel, productExtend?.Producttype ?? "generic", rates.Count);
        }

        return products;
    }

    /// <summary>
    /// Extract time series for given GSRNs from a start date.
    /// </summary>
    public async Task<List<ExtractedTimeSeries>> ExtractTimeSeriesAsync(
        List<string> gsrns, DateTimeOffset startDate)
    {
        var result = new List<ExtractedTimeSeries>();

        foreach (var gsrn in gsrns)
        {
            // Find time series for this metering point
            var series = await _db.EmsTimeseries
                .Where(ts => ts.Dataareaid == DataAreaId
                    && ts.Meteringpoint == gsrn
                    && ts.Deliverycategory == DeliveryCategory)
                .ToListAsync();

            foreach (var ts in series)
            {
                var values = await _db.EmsTimeseriesValues
                    .Where(v => v.Dataareaid == DataAreaId
                        && v.Timeseriesrefrecid == ts.Recid
                        && v.Timeofvalue >= startDate.DateTime)
                    .OrderBy(v => v.Timeofvalue)
                    .ToListAsync();

                if (values.Count == 0) continue;

                result.Add(new ExtractedTimeSeries
                {
                    Gsrn = gsrn,
                    PeriodStart = ToUtc(values.First().Timeofvalue),
                    PeriodEnd = ToUtc(values.Last().Timeofvalue.AddHours(1)),
                    Resolution = ts.Timeresolution == 60 ? "PT1H" : "PT15M",
                    Observations = values.Select(v => new ExtractedObservation
                    {
                        Timestamp = ToUtc(v.Timeofvalue),
                        Kwh = v.Value,
                        Quality = MapQuality(v.Qualityofvalue)
                    }).ToList()
                });
            }
        }

        _logger.LogInformation("Extracted {Count} time series for {GsrnCount} metering points",
            result.Count, gsrns.Count);
        return result;
    }

    /// <summary>
    /// Extract invoiced settlements from FlexBillingHistory.
    /// Each billing period = one settlement with status Invoiced.
    /// </summary>
    public async Task<List<ExtractedSettlement>> ExtractSettlementsAsync(List<string> gsrns)
    {
        var result = new List<ExtractedSettlement>();

        foreach (var gsrn in gsrns)
        {
            // Get billing history entries for this metering point
            var historyEntries = await _db.FlexBillingHistoryTables
                .Where(h => h.DataAreaId == DataAreaId
                    && h.MeteringPoint == gsrn
                    && h.DeliveryCategory == DeliveryCategory)
                .OrderBy(h => h.ReqStartDate)
                .ToListAsync();

            foreach (var entry in historyEntries)
            {
                // Get hourly lines for this billing period
                var lines = await _db.FlexBillingHistoryLines
                    .Where(l => l.DataAreaId == DataAreaId
                        && l.HistKeyNumber == entry.HistKeyNumber)
                    .OrderBy(l => l.DateTime24Hour)
                    .ToListAsync();

                if (lines.Count == 0) continue;

                // Derive period start: use ReqStartDate if real, otherwise first hourly line
                var periodStart = entry.ReqStartDate != NoEndDate && entry.ReqStartDate.Year > 1901
                    ? entry.ReqStartDate
                    : lines.Min(l => l.DateTime24Hour);

                var totalEnergy = lines.Sum(l => l.TimeValue);
                var electricityAmount = lines.Sum(l => l.TimeValue * l.CalculatedPrice);
                var spotAmount = lines.Sum(l => l.TimeValue * l.PowerExchangePrice);
                var marginAmount = electricityAmount - spotAmount;

                // Get tariffs for this metering point and period (use derived periodStart, not sentinel)
                var tariffLines = await ExtractTariffLinesAsync(gsrn, periodStart, lines);

                var totalAmount = electricityAmount + tariffLines.Sum(t => t.AmountDkk);

                result.Add(new ExtractedSettlement
                {
                    Gsrn = gsrn,
                    PeriodStart = periodStart,
                    PeriodEnd = entry.ReqEndDate,
                    BillingLogNum = entry.BillingLogNum,
                    HistKeyNumber = entry.HistKeyNumber,
                    TotalEnergyKwh = totalEnergy,
                    ElectricityAmountDkk = electricityAmount,
                    SpotAmountDkk = spotAmount,
                    MarginAmountDkk = marginAmount,
                    TariffLines = tariffLines,
                    TotalAmountDkk = totalAmount
                });
            }
        }

        _logger.LogInformation("Extracted {Count} settlements for {GsrnCount} metering points",
            result.Count, gsrns.Count);
        return result;
    }

    private async Task<List<ExtractedTariffLine>> ExtractTariffLinesAsync(
        string gsrn, DateTime forDate, List<FlexBillingHistoryLine> hourlyData)
    {
        var tariffLines = new List<ExtractedTariffLine>();
        var forDateOnly = forDate.Date;

        // Get active tariff assignments for this metering point
        // Aligned with CorrectionService.GetTariffsAsync:
        // - ChargeTypeCode == 3 (tariffs only, excludes abonnementer/gebyrer)
        // - Group by PartyChargeTypeId in the query to avoid duplicates
        var tariffAssignments = await (
            from pec in _db.PriceElementChecks
            join pecd in _db.PriceElementCheckData
                on new { pec.DataAreaId, RefRecId = pec.RecId }
                equals new { pecd.DataAreaId, RefRecId = pecd.PriceElementCheckRefRecId }
            join pet in _db.PriceElementTables
                on new { pecd.DataAreaId, pecd.PartyChargeTypeId }
                equals new { pet.DataAreaId, pet.PartyChargeTypeId }
            where pec.DataAreaId == DataAreaId
                && pec.MeteringPointId == gsrn
                && pec.DeliveryCategory == DeliveryCategory
                && pet.ChargeTypeCode == 3 // Tariff only (same as CorrectionService)
                && pecd.StartDate <= forDateOnly
                && (pecd.EndDate == NoEndDate || pecd.EndDate >= forDateOnly)
            group pet by pecd.PartyChargeTypeId into g
            select new { PartyChargeTypeId = g.Key, Description = g.First().Description }
        ).ToListAsync();

        foreach (var tariff in tariffAssignments)
        {
            // Get the most recent rate for this tariff
            var rate = await _db.PriceElementRates
                .Where(r => r.DataAreaId == DataAreaId
                    && r.PartyChargeTypeId == tariff.PartyChargeTypeId
                    && r.StartDate <= forDateOnly)
                .OrderByDescending(r => r.StartDate)
                .FirstOrDefaultAsync();

            if (rate == null) continue;

            // Calculate tariff amount using hourly rates
            // Same logic as CorrectionService: hourlyRate > 0 ? hourlyRate : rate.Price
            decimal totalTariffAmount = 0m;
            decimal totalTariffEnergy = 0m;

            foreach (var line in hourlyData)
            {
                var hourNum = line.DateTime24Hour.Hour + 1; // Convert 0-23 to 1-24
                var hourlyRate = rate.GetPriceForHour(hourNum);
                var effectiveRate = hourlyRate > 0 ? hourlyRate : rate.Price;

                if (effectiveRate <= 0) continue;

                totalTariffAmount += line.TimeValue * effectiveRate;
                totalTariffEnergy += line.TimeValue;
            }

            if (totalTariffAmount == 0) continue;

            tariffLines.Add(new ExtractedTariffLine
            {
                PartyChargeTypeId = tariff.PartyChargeTypeId,
                Description = tariff.Description,
                AmountDkk = totalTariffAmount,
                EnergyKwh = totalTariffEnergy,
                AvgUnitPrice = totalTariffEnergy != 0 ? totalTariffAmount / totalTariffEnergy : 0
            });
        }

        return tariffLines;
    }

    /// <summary>
    /// Get the GSRN — prefer the GSRN column, fall back to METERINGPOINT.
    /// Xellent stores the 18-digit number in METERINGPOINT for older records.
    /// </summary>
    private static string GetGsrn(ExuDelpoint delpoint)
    {
        var gsrn = delpoint.Gsrn?.Trim();
        if (!string.IsNullOrEmpty(gsrn)) return gsrn;
        return delpoint.Meteringpoint?.Trim() ?? "";
    }

    private static string MapGridArea(string powerExchangeArea)
        => powerExchangeArea?.Trim() switch
        {
            "DK1" => "DK1",
            "DK2" => "DK2",
            "DK Vest" => "DK1",
            "DK Øst" => "DK2",
            _ => "DK1" // Default
        };

    private static string MapQuality(int qualityOfValue)
        => qualityOfValue switch
        {
            36 => "A01",  // Measured
            56 => "A02",  // Estimated
            _ => "A01"
        };

    /// <summary>
    /// Convert a Danish local DateTime to UTC DateTimeOffset.
    /// Xellent stores all dates in Danish local time (CET/CEST).
    /// </summary>
    private static DateTimeOffset ToUtc(DateTime localDate)
    {
        if (localDate.Kind == DateTimeKind.Utc) return localDate;
        var offset = DanishTz.GetUtcOffset(localDate);
        return new DateTimeOffset(DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified), offset).ToUniversalTime();
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
