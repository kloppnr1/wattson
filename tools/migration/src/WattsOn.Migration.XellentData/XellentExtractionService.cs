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
                            XellentMeteringPoint = NullIfEmpty(firstMp.Delpoint.Meteringpoint),
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
    /// Enrich metering point product periods with addon products from the ProductExtend chain.
    /// These are rate types like "Grøn strøm" that exist as separate SupplierProducts
    /// but need SupplyProductPeriods to link them to the supply.
    /// Call AFTER ExtractDistinctProductsAsync so we know which rate types are primary vs addon.
    /// </summary>
    public async Task EnrichWithAddonProductPeriodsAsync(
        List<ExtractedCustomer> customers, HashSet<string> addonProductNames)
    {
        if (addonProductNames.Count == 0) return;

        foreach (var customer in customers)
        {
            foreach (var mp in customer.MeteringPoints)
            {
                var gsrn = mp.XellentMeteringPoint ?? mp.Gsrn;

                // Query the full chain for this metering point to find addon product periods
                var addonPeriods = await (
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
                        && dp.Meteringpoint == gsrn
                        && dp.Deliverycategory == DeliveryCategory
                        && inv.ItemType == 2
                        && inv.ExuUseRateFromFlexPricing == 0
                        && addonProductNames.Contains(pe.Producttype)
                    select new
                    {
                        ProductType = pe.Producttype,
                        CpStart = cp.Startdate,
                        CpEnd = cp.Enddate,
                        PeStart = pe.Startdate,
                        PeEnd = pe.Enddate
                    }
                ).ToListAsync();

                foreach (var ap in addonPeriods)
                {
                    // Effective period = intersection of ContractPart and ProductExtend validity
                    var start = ap.CpStart > ap.PeStart ? ap.CpStart : ap.PeStart;
                    var cpEnd = ap.CpEnd == NoEndDate ? (DateTime?)null : ap.CpEnd;
                    var peEnd = ap.PeEnd == NoEndDate ? (DateTime?)null : ap.PeEnd;
                    DateTimeOffset? end = (cpEnd, peEnd) switch
                    {
                        (null, null) => null,
                        (DateTime a, null) => ToUtc(a),
                        (null, DateTime b) => ToUtc(b),
                        (DateTime a, DateTime b) => ToUtc(a < b ? a : b),
                    };

                    // Skip invalid periods where end < start (addon was deactivated before the ContractPart started)
                    if (end.HasValue && end.Value < ToUtc(start)) continue;

                    mp.ProductPeriods.Add(new ExtractedProductPeriod
                    {
                        ProductName = ap.ProductType,
                        Start = ToUtc(start),
                        End = end,
                    });
                }

                // Deduplicate and sort
                mp.ProductPeriods = mp.ProductPeriods
                    .DistinctBy(pp => (pp.ProductName, pp.Start))
                    .OrderBy(pp => pp.Start)
                    .ToList();
            }
        }
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

            var rates = new List<ExuRateTable>();
            if (productExtend != null)
            {
                // Try 1: Generic rates (Productnum == "") — most common
                rates = await _db.ExuRateTables
                    .Where(r => r.Dataareaid == DataAreaId
                        && r.Deliverycategory == DeliveryCategory
                        && r.Ratetype == productExtend.Producttype
                        && r.Productnum == "")
                    .OrderBy(r => r.Startdate)
                    .ToListAsync();

                // Try 2: Product-specific rates via ProductExtend type (Productnum == productNum)
                if (rates.Count == 0 || rates.All(r => r.Rate == 0))
                {
                    var specific = await _db.ExuRateTables
                        .Where(r => r.Dataareaid == DataAreaId
                            && r.Deliverycategory == DeliveryCategory
                            && r.Ratetype == productExtend.Producttype
                            && r.Productnum == productNum)
                        .OrderBy(r => r.Startdate)
                        .ToListAsync();
                    if (specific.Count > 0 && specific.Any(r => r.Rate != 0))
                        rates = specific;
                }

                // Try 3: Self-referencing rates (Ratetype=productNum, Productnum=productNum)
                // Some products like V Kvartal store rates under their own name, not the ProductExtend type
                if (rates.Count == 0 || rates.All(r => r.Rate == 0))
                {
                    var selfRates = await _db.ExuRateTables
                        .Where(r => r.Dataareaid == DataAreaId
                            && r.Deliverycategory == DeliveryCategory
                            && r.Ratetype == productNum
                            && r.Productnum == productNum)
                        .OrderBy(r => r.Startdate)
                        .ToListAsync();

                    if (selfRates.Count > 0 && selfRates.Any(r => r.Rate != 0))
                    {
                        rates = selfRates;
                        _logger.LogInformation("Product {Name}: found {Count} self-referencing rates (Ratetype=Productnum='{Name}')",
                            productNum, rates.Count, productNum);
                    }
                }

                // Try 4: Any rates for the ProductExtend rate type (no Productnum filter)
                if (rates.Count == 0 || rates.All(r => r.Rate == 0))
                {
                    var anyRates = await _db.ExuRateTables
                        .Where(r => r.Dataareaid == DataAreaId
                            && r.Deliverycategory == DeliveryCategory
                            && r.Ratetype == productExtend.Producttype)
                        .OrderBy(r => r.Startdate)
                        .ToListAsync();
                    if (anyRates.Count > 0 && anyRates.Any(r => r.Rate != 0))
                        rates = anyRates;
                }

                _logger.LogInformation("Product {Name}: {Model}, rateType={RateType}, {RateCount} rates from ExuRateTable",
                    productNum, pricingModel, productExtend.Producttype, rates.Count);
            }
            else
            {
                // No ProductExtend — try self-referencing rates as last resort
                var selfRates = await _db.ExuRateTables
                    .Where(r => r.Dataareaid == DataAreaId
                        && r.Deliverycategory == DeliveryCategory
                        && r.Ratetype == productNum
                        && r.Productnum == productNum)
                    .OrderBy(r => r.Startdate)
                    .ToListAsync();

                if (selfRates.Count > 0)
                {
                    rates = selfRates;
                    _logger.LogInformation("Product {Name}: no ProductExtend, but found {Count} self-referencing rates",
                        productNum, rates.Count);
                }
                else
                {
                    _logger.LogWarning("Product {Name}: no ExuProductExtendTable entry and no rates found — rates will be derived from billing history",
                        productNum);
                }
            }

            products.Add(new ExtractedProduct
            {
                Name = productNum,
                PricingModel = pricingModel,
                // Filter zero rates, then deduplicate by StartDate (keep highest RecId as tie-breaker)
                Rates = rates
                    .Where(r => r.Rate != 0 || rates.All(rr => rr.Rate == 0))
                    .GroupBy(r => r.Startdate)
                    .Select(g => g.OrderByDescending(r => r.Recid).First())
                    .OrderBy(r => r.Startdate)
                    .Select(r => new ExtractedRate
                    {
                        StartDate = ToUtc(r.Startdate),
                        RateDkkPerKwh = r.Rate,
                    }).ToList()
            });
        }

        // Also extract secondary product rate types from the full margin chain.
        // These are rate types like "Grøn strøm", "Leje af plads" that come through:
        // ContractPart → ProductExtend → InventTable(ItemType=2, UseRateFromFlexPricing=0) → RateTable
        // They exist as separate price components applied alongside the main product margin.
        var existingNames = products.Select(p => p.Name).ToHashSet();

        // Collect primary rate types already used by main products — skip these as addons
        // e.g. "Handelsomkostninger" is V Kvartal's primary rate type, "V Klima" is V Fordel U's
        var primaryRateTypes = new HashSet<string>();
        foreach (var productNum in productNums)
        {
            var pe = await _db.ExuProductExtendTables
                .Where(p => p.DataAreaId == DataAreaId && p.Productnum == productNum)
                .OrderByDescending(p => p.Startdate)
                .FirstOrDefaultAsync();
            if (pe != null) primaryRateTypes.Add(pe.Producttype);
        }

        var allProductTypes = await (
            from cust in _db.CustTables
            join contract in _db.ExuContractTables
                on new { cust.Accountnum, cust.Dataareaid }
                equals new { Accountnum = contract.Custaccount, contract.Dataareaid }
            join cp in _db.ExuContractPartTables
                on new { contract.Contractnum, contract.Dataareaid }
                equals new { cp.Contractnum, cp.Dataareaid }
            join pe in _db.ExuProductExtendTables
                on new { Dataareaid = cp.Dataareaid, cp.Productnum }
                equals new { Dataareaid = pe.DataAreaId, pe.Productnum }
            join inv in _db.InventTables
                on new { DataAreaId = pe.DataAreaId, ItemId = pe.Producttype }
                equals new { inv.DataAreaId, inv.ItemId }
            where cust.Dataareaid == DataAreaId
                && accountNumbers.Contains(cust.Accountnum)
                && contract.Deliverycategory == DeliveryCategory
                && inv.ItemType == 2
                && inv.ExuUseRateFromFlexPricing == 0
            select new { pe.Producttype, cp.Productnum }
        ).Distinct().ToListAsync();

        foreach (var pt in allProductTypes)
        {
            // Skip if already extracted as a product name OR if it's a primary rate type of a main product
            // e.g. "Handelsomkostninger" is V Kvartal's primary rate type — not a separate addon
            if (existingNames.Contains(pt.Producttype) || primaryRateTypes.Contains(pt.Producttype)) continue;

            var rates = await _db.ExuRateTables
                .Where(r => r.Dataareaid == DataAreaId
                    && r.Deliverycategory == DeliveryCategory
                    && r.Ratetype == pt.Producttype
                    && r.Productnum == "")
                .OrderBy(r => r.Startdate)
                .ToListAsync();

            if (rates.Count == 0) continue;

            products.Add(new ExtractedProduct
            {
                Name = pt.Producttype,
                Description = $"Tillæg via {pt.Productnum}",
                PricingModel = "SpotAddon", // These are per-kWh rate components
                Rates = rates
                    .Where(r => r.Rate != 0 || rates.All(rr => rr.Rate == 0))
                    .Select(r => new ExtractedRate
                    {
                        StartDate = ToUtc(r.Startdate),
                        RateDkkPerKwh = r.Rate,
                    }).ToList()
            });

            existingNames.Add(pt.Producttype);
            _logger.LogInformation("Product margin component {Type}: {Count} rates (via product {Product})",
                pt.Producttype, rates.Count, pt.Productnum);
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
    public async Task<List<ExtractedSettlement>> ExtractSettlementsAsync(
        List<(string Gsrn, string? XellentMeteringPoint)> meteringPoints)
    {
        var result = new List<ExtractedSettlement>();

        foreach (var (gsrn, xellentMp) in meteringPoints)
        {
            // Try GSRN first, fall back to Xellent METERINGPOINT column
            var searchIds = new List<string> { gsrn };
            if (!string.IsNullOrEmpty(xellentMp) && xellentMp != gsrn)
                searchIds.Add(xellentMp);

            // Get ALL billing history entries for this metering point.
            // No date filter — settlements are correction baselines regardless of age.
            // ReqStartDate is often 1900-01-01 (sentinel) — actual period dates come from hourly lines.
            var historyEntries = await _db.FlexBillingHistoryTables
                .Where(h => h.DataAreaId == DataAreaId
                    && searchIds.Contains(h.MeteringPoint)
                    && h.DeliveryCategory == DeliveryCategory)
                .OrderBy(h => h.ReqStartDate)
                .ToListAsync();

            _logger.LogInformation("Found {Count} billing entries for {Mp} (searched: [{Ids}])",
                historyEntries.Count, gsrn, string.Join(", ", searchIds));

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

                // Build hourly provenance lines
                var hourlyLines = lines.Select(l => new HourlyLine
                {
                    Timestamp = ToUtc(l.DateTime24Hour),
                    Kwh = l.TimeValue,
                    SpotPriceDkkPerKwh = l.PowerExchangePrice,
                    CalculatedPriceDkkPerKwh = l.CalculatedPrice,
                    SpotAmountDkk = l.TimeValue * l.PowerExchangePrice,
                    MarginAmountDkk = l.TimeValue * (l.CalculatedPrice - l.PowerExchangePrice),
                    ElectricityAmountDkk = l.TimeValue * l.CalculatedPrice
                }).ToList();

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
                    TotalAmountDkk = totalAmount,
                    HourlyLines = hourlyLines
                });
            }
        }

        _logger.LogInformation("Extracted {Count} settlements for {MpCount} metering points",
            result.Count, meteringPoints.Count);
        return result;
    }

    /// <summary>
    /// Extract DataHub charges (prices) for given metering points.
    /// Reads PriceElementCheck → PriceElementCheckData → PriceElementTable → PriceElementRates.
    /// Returns unique charges (deduped by PartyChargeTypeId) with their hourly rate history.
    /// </summary>
    public async Task<List<ExtractedPrice>> ExtractPricesAsync(List<string> gsrns)
    {
        var result = new Dictionary<string, ExtractedPrice>();

        foreach (var gsrn in gsrns)
        {
            // Get charge assignments with the ACTUAL owner GLN from PriceElementCheckData.OwnerId.
            // OwnerId is the GridCompanyId (GLN as numeric) that identifies which grid operator's
            // rates apply to this metering point.
            // Group by (ChargeTypeId, ChargeTypeCode, OwnerId) to capture grid operator switches —
            // e.g. customer 405013 switched from GLN 5790000681372 to 5790001089030 on 2023-03-01.
            // Each owner gets its own Price entry with its own rate history.
            var assignments = await (
                from pec in _db.PriceElementChecks
                join pecd in _db.PriceElementCheckData
                    on new { pec.DataAreaId, RefRecId = pec.RecId }
                    equals new { pecd.DataAreaId, RefRecId = pecd.PriceElementCheckRefRecId }
                join pet in _db.PriceElementTables
                    on new { pecd.DataAreaId, pecd.PartyChargeTypeId, pecd.ChargeTypeCode, GridCompanyId = pecd.OwnerId }
                    equals new { pet.DataAreaId, pet.PartyChargeTypeId, pet.ChargeTypeCode, pet.GridCompanyId }
                where pec.DataAreaId == DataAreaId
                    && pec.MeteringPointId == gsrn
                    && pec.DeliveryCategory == DeliveryCategory
                group new { pecd, pet } by new { pecd.PartyChargeTypeId, pecd.ChargeTypeCode, pecd.OwnerId } into g
                select new
                {
                    PartyChargeTypeId = g.Key.PartyChargeTypeId,
                    Description = g.First().pet.Description,
                    ChargeTypeCode = g.Key.ChargeTypeCode,
                    OwnerGln = g.Key.OwnerId,
                    EarliestStart = g.Min(x => x.pecd.StartDate),
                    LatestEnd = g.Max(x => x.pecd.EndDate)
                }
            ).ToListAsync();

            foreach (var assignment in assignments)
            {
                // Key by PartyChargeTypeId + ChargeTypeCode + OwnerGln to keep each grid operator separate
                var ownerGln = ((long)assignment.OwnerGln).ToString();
                var resultKey = $"{assignment.PartyChargeTypeId}:{assignment.ChargeTypeCode}:{ownerGln}";
                if (result.ContainsKey(resultKey))
                    continue;

                // Get rate entries filtered by:
                //   - PartyChargeTypeId + ChargeTypeCode (charge identity)
                //   - GridCompanyId = OwnerId (correct grid operator for this metering point)
                //   - ValidInMarketToExcl = 1900-01-01 (only active/current versions, not superseded)
                // Then dedup by StartDate (take highest RecId if still duplicated across CompanyId)
                var rates = await _db.PriceElementRates
                    .Where(r => r.DataAreaId == DataAreaId
                        && r.PartyChargeTypeId == assignment.PartyChargeTypeId
                        && r.ChargeTypeCode == assignment.ChargeTypeCode
                        && r.GridCompanyId == assignment.OwnerGln
                        && r.ValidInMarketToExcl == NoEndDate)
                    .OrderBy(r => r.StartDate)
                    .ThenByDescending(r => r.RecId)
                    .ToListAsync();

                // Dedup: keep one rate per StartDate (highest RecId wins)
                rates = rates
                    .GroupBy(r => r.StartDate)
                    .Select(g => g.First()) // Already sorted by RecId desc within group
                    .OrderBy(r => r.StartDate)
                    .ToList();

                if (rates.Count == 0)
                {
                    _logger.LogWarning("No rates found for charge {ChargeId} ({Desc}) owner {Gln}",
                        assignment.PartyChargeTypeId, assignment.Description, ownerGln);
                    continue;
                }

                _logger.LogInformation("Charge {ChargeId}:{ChargeType} owner={Gln}: {Count} rate periods (was {Raw} before dedup)",
                    assignment.PartyChargeTypeId, assignment.ChargeTypeCode, ownerGln,
                    rates.Count, rates.Count); // After dedup

                // Detect if this is an hourly-differentiated tariff
                // (any rate entry with non-zero Price2..Price24 means hourly)
                var isHourly = rates.Any(r => HasHourlyRates(r));

                // Build price points
                var points = new List<ExtractedPricePoint>();
                foreach (var rate in rates)
                {
                    if (isHourly && assignment.ChargeTypeCode == 3) // Tariff with hourly rates
                    {
                        // Expand 24 hourly prices into individual price points
                        // Each rate entry covers from its StartDate forward
                        for (int hour = 0; hour < 24; hour++)
                        {
                            var hourlyRate = rate.GetPriceForHour(hour + 1);
                            var effectiveRate = hourlyRate > 0 ? hourlyRate : rate.Price;
                            points.Add(new ExtractedPricePoint
                            {
                                Timestamp = ToUtc(rate.StartDate.Date.AddHours(hour)),
                                Price = effectiveRate
                            });
                        }
                    }
                    else if (assignment.ChargeTypeCode == 1) // Subscription
                    {
                        // Subscription rates in Xellent are MONTHLY amounts.
                        // WattsOn's engine calculates subscriptions as: dailyRate × daysInPeriod.
                        // Convert monthly → daily by dividing by days in the rate's effective month.
                        var daysInMonth = DateTime.DaysInMonth(rate.StartDate.Year, rate.StartDate.Month);
                        points.Add(new ExtractedPricePoint
                        {
                            Timestamp = ToUtc(rate.StartDate),
                            Price = rate.Price / daysInMonth
                        });
                    }
                    else
                    {
                        // Flat rate — single price point per effective date
                        points.Add(new ExtractedPricePoint
                        {
                            Timestamp = ToUtc(rate.StartDate),
                            Price = rate.Price
                        });
                    }
                }

                var chargeType = MapChargeType(assignment.ChargeTypeCode);
                var category = ClassifyCharge(assignment.PartyChargeTypeId, assignment.Description, assignment.ChargeTypeCode);

                result[resultKey] = new ExtractedPrice
                {
                    ChargeId = assignment.PartyChargeTypeId,
                    OwnerGln = ownerGln,
                    Type = chargeType,
                    Description = assignment.Description.Trim(),
                    EffectiveDate = ToUtc(rates.Min(r => r.StartDate)),
                    Resolution = isHourly && assignment.ChargeTypeCode == 3 ? "PT1H" : null,
                    IsTax = category == "Elafgift",
                    IsPassThrough = category is "Systemtarif" or "Transmissionstarif" or "Balancetarif",
                    Category = category,
                    ChargeTypeCode = assignment.ChargeTypeCode,
                    Points = points
                };
            }
        }

        _logger.LogInformation("Extracted {Count} unique charges for {GsrnCount} metering points",
            result.Count, gsrns.Count);
        return result.Values.ToList();
    }

    /// <summary>
    /// Extract price-to-metering-point links from PriceElementCheck + PriceElementCheckData.
    /// </summary>
    public async Task<List<ExtractedPriceLink>> ExtractPriceLinksAsync(List<string> gsrns)
    {
        var links = new List<ExtractedPriceLink>();

        foreach (var gsrn in gsrns)
        {
            // Pull raw rows into memory, then group — small dataset per GSRN,
            // and allows sentinel handling in C# without complex SQL translation.
            var rawRows = await (
                from pec in _db.PriceElementChecks
                join pecd in _db.PriceElementCheckData
                    on new { pec.DataAreaId, RefRecId = pec.RecId }
                    equals new { pecd.DataAreaId, RefRecId = pecd.PriceElementCheckRefRecId }
                join pet in _db.PriceElementTables
                    on new { pecd.DataAreaId, pecd.PartyChargeTypeId, pecd.ChargeTypeCode, GridCompanyId = pecd.OwnerId }
                    equals new { pet.DataAreaId, pet.PartyChargeTypeId, pet.ChargeTypeCode, pet.GridCompanyId }
                where pec.DataAreaId == DataAreaId
                    && pec.MeteringPointId == gsrn
                    && pec.DeliveryCategory == DeliveryCategory
                select new
                {
                    pecd.PartyChargeTypeId,
                    pecd.ChargeTypeCode,
                    pecd.OwnerId,
                    pet.Description,
                    pecd.StartDate,
                    pecd.EndDate
                }
            ).ToListAsync();

            var assignments = rawRows
                .GroupBy(r => new { r.PartyChargeTypeId, r.ChargeTypeCode, r.OwnerId })
                .Select(g =>
                {
                    var nonSentinelStarts = g.Where(x => x.StartDate > NoEndDate).Select(x => x.StartDate).ToList();
                    return new
                    {
                        PartyChargeTypeId = g.Key.PartyChargeTypeId,
                        ChargeTypeCode = g.Key.ChargeTypeCode,
                        OwnerGln = g.Key.OwnerId,
                        Description = g.First().Description,
                        EarliestStart = nonSentinelStarts.Count > 0 ? nonSentinelStarts.Min() : g.Min(x => x.StartDate),
                        HasOpenEnd = g.Any(x => x.EndDate <= NoEndDate),
                        LatestEnd = g.Max(x => x.EndDate)
                    };
                })
                .ToList();

            foreach (var a in assignments)
            {
                var ownerGln = ((long)a.OwnerGln).ToString();
                var effectiveStart = a.EarliestStart;
                var effectiveEnd = a.HasOpenEnd ? (DateTime?)null : (DateTime?)a.LatestEnd;

                links.Add(new ExtractedPriceLink
                {
                    Gsrn = gsrn,
                    ChargeId = a.PartyChargeTypeId,
                    ChargeTypeCode = a.ChargeTypeCode,
                    OwnerGln = ownerGln,
                    EffectiveDate = ToUtc(effectiveStart),
                    EndDate = effectiveEnd.HasValue ? ToUtc(effectiveEnd.Value) : null
                });
            }
        }

        // Detect grid operator switches and fix link boundaries.
        // When the same chargeId exists under different owners (grid operator switch),
        // the old owner's links should end when the new owner's start.
        // For unique-to-old-operator charges, detect the switch date from shared charges.
        foreach (var gsrnGroup in links.GroupBy(l => l.Gsrn))
        {
            var gsrnLinks = gsrnGroup.ToList();

            // 1) Same chargeId, different owners → cap old owner at new owner's start
            foreach (var chargeGroup in gsrnLinks.GroupBy(l => l.ChargeId).Where(g => g.Select(l => l.OwnerGln).Distinct().Count() > 1))
            {
                var byOwner = chargeGroup
                    .GroupBy(l => l.OwnerGln)
                    .Select(g => new { Owner = g.Key, Start = g.Min(l => l.EffectiveDate), Links = g.ToList() })
                    .OrderBy(o => o.Start)
                    .ToList();

                for (var i = 0; i < byOwner.Count - 1; i++)
                {
                    var cap = byOwner[i + 1].Start;
                    foreach (var link in byOwner[i].Links.Where(l => l.EndDate == null || l.EndDate > cap))
                    {
                        _logger.LogInformation("Capped {Charge} (owner {Owner}) at owner switch {Date:yyyy-MM-dd}", link.ChargeId, link.OwnerGln, cap);
                        link.EndDate = cap;
                    }
                }
            }

            // Note: we intentionally do NOT cap open-ended links from old grid operators
            // that have unique chargeIds (e.g. 22000 Abon-Net, 22100 Trans-lokalnet).
            // These charges may continue after a grid operator switch even though
            // the owner didn't change — only charges with the SAME chargeId under
            // a new owner get capped (handled by Part 1 above).
        }

        _logger.LogInformation("Extracted {Count} price links for {GsrnCount} metering points",
            links.Count, gsrns.Count);
        return links;
    }

    private static bool HasHourlyRates(PriceElementRates rate)
    {
        // If any of Price2..Price24 is non-zero, this has hourly differentiation
        return rate.Price2 != 0 || rate.Price3 != 0 || rate.Price4 != 0 ||
               rate.Price5 != 0 || rate.Price6 != 0 || rate.Price7 != 0 ||
               rate.Price8 != 0 || rate.Price9 != 0 || rate.Price10 != 0 ||
               rate.Price11 != 0 || rate.Price12 != 0 || rate.Price13 != 0 ||
               rate.Price14 != 0 || rate.Price15 != 0 || rate.Price16 != 0 ||
               rate.Price17 != 0 || rate.Price18 != 0 || rate.Price19 != 0 ||
               rate.Price20 != 0 || rate.Price21 != 0 || rate.Price22 != 0 ||
               rate.Price23 != 0 || rate.Price24 != 0;
    }

    private static string MapChargeType(int chargeTypeCode) => chargeTypeCode switch
    {
        1 => "Abonnement",  // Subscription
        2 => "Gebyr",       // Fee
        3 => "Tarif",       // Tariff
        _ => "Tarif"
    };

    /// <summary>
    /// Classify charge based on PartyChargeTypeId patterns and description.
    /// Aligns with WattsOn PriceCategory enum.
    /// </summary>
    private static string ClassifyCharge(string chargeId, string description, int chargeTypeCode)
    {
        var descLower = description.ToLowerInvariant();
        var idLower = chargeId.ToLowerInvariant();

        if (descLower.Contains("elafgift")) return "Elafgift";
        if (descLower.Contains("systemtarif")) return "Systemtarif";
        if (descLower.Contains("transmiss")) return "Transmissionstarif";
        if (descLower.Contains("balance")) return "Balancetarif";
        if (descLower.Contains("nettarif") || descLower.Contains("net-tarif")) return "Nettarif";
        if (chargeTypeCode == 1 && descLower.Contains("abonne")) return "NetAbonnement";
        if (descLower.Contains("net ") || descLower.Contains("net-")) return "Nettarif";

        return "Andet";
    }

    /// <summary>
    /// Infer owner GLN from charge ID patterns.
    /// - 40000/41000/42000/45xxx etc. → Energinet (5790000432752)
    /// - EA-xxx → Tax authority / Energinet
    /// - Everything else → grid operator (default: N1 5790001089030)
    /// </summary>
    private static string InferOwnerGln(string chargeId, string description)
    {
        var descLower = description.ToLowerInvariant();

        // Energinet charges (system/transmission/balance tariffs)
        if (descLower.Contains("systemtarif") || descLower.Contains("transmiss") ||
            descLower.Contains("balance"))
            return "5790000432752"; // Energinet GLN

        // Elafgift
        if (descLower.Contains("elafgift") || chargeId.StartsWith("EA", StringComparison.OrdinalIgnoreCase))
            return "5790000432752"; // Energinet GLN (collects on behalf of state)

        // Numeric IDs starting with 4xxxx are often Energinet
        if (chargeId.Length >= 5 && chargeId.StartsWith("4") && int.TryParse(chargeId, out _))
        {
            // 40000-series: could be Energinet system tariffs
            // But some grid operators also use numeric IDs
            // Default to grid operator for safety
        }

        // Default: grid operator (N1/Radius etc.)
        return "5790001089030"; // N1 GLN (most common for Verdo area)
    }

    private async Task<List<ExtractedTariffLine>> ExtractTariffLinesAsync(
        string gsrn, DateTime forDate, List<FlexBillingHistoryLine> hourlyData)
    {
        var tariffLines = new List<ExtractedTariffLine>();
        var forDateOnly = forDate.Date;

        // Get active tariff assignments for this metering point
        // Aligned with CorrectionService.GetTariffsAsync:
        // - ChargeTypeCode == 3 (tariffs only, excludes abonnementer/gebyrer)
        // - Join on BOTH PartyChargeTypeId AND ChargeTypeCode
        // - Group by PartyChargeTypeId to avoid duplicates
        var tariffAssignments = await (
            from pec in _db.PriceElementChecks
            join pecd in _db.PriceElementCheckData
                on new { pec.DataAreaId, RefRecId = pec.RecId }
                equals new { pecd.DataAreaId, RefRecId = pecd.PriceElementCheckRefRecId }
            join pet in _db.PriceElementTables
                on new { pecd.DataAreaId, pecd.PartyChargeTypeId, pecd.ChargeTypeCode, GridCompanyId = pecd.OwnerId }
                equals new { pet.DataAreaId, pet.PartyChargeTypeId, pet.ChargeTypeCode, pet.GridCompanyId }
            where pec.DataAreaId == DataAreaId
                && pec.MeteringPointId == gsrn
                && pec.DeliveryCategory == DeliveryCategory
                && pecd.ChargeTypeCode == 3 // Tariff only
                && pecd.StartDate <= forDateOnly
                && (pecd.EndDate == NoEndDate || pecd.EndDate >= forDateOnly)
            group new { pet, pecd } by new { pecd.PartyChargeTypeId, pecd.OwnerId } into g
            select new
            {
                PartyChargeTypeId = g.Key.PartyChargeTypeId,
                Description = g.First().pet.Description,
                OwnerGln = g.Key.OwnerId
            }
        ).ToListAsync();

        foreach (var tariff in tariffAssignments)
        {
            // Count all candidate rates for provenance — filter by ChargeTypeCode + GridCompanyId
            var candidateRates = await _db.PriceElementRates
                .Where(r => r.DataAreaId == DataAreaId
                    && r.PartyChargeTypeId == tariff.PartyChargeTypeId
                    && r.ChargeTypeCode == 3 // Tariff rates only
                    && r.GridCompanyId == tariff.OwnerGln
                    && r.StartDate <= forDateOnly)
                .OrderByDescending(r => r.StartDate)
                .ToListAsync();

            if (candidateRates.Count == 0) continue;

            var rate = candidateRates[0]; // Most recent
            var isHourly = HasHourlyRates(rate);

            // Build provenance: capture the exact rate row and its values
            var hourlyRates = isHourly ? new decimal[24] : null;
            if (isHourly)
            {
                for (int h = 1; h <= 24; h++)
                    hourlyRates![h - 1] = rate.GetPriceForHour(h);
            }

            var provenance = new TariffRateProvenance
            {
                PartyChargeTypeId = tariff.PartyChargeTypeId,
                RateStartDate = rate.StartDate,
                IsHourly = isHourly,
                FlatRate = rate.Price,
                HourlyRates = hourlyRates,
                CandidateRateCount = candidateRates.Count,
                SelectionRule = $"Most recent rate with StartDate <= {forDateOnly:yyyy-MM-dd} " +
                    $"(selected {rate.StartDate:yyyy-MM-dd} from {candidateRates.Count} candidates)"
            };

            // Calculate tariff amount with hourly detail
            decimal totalTariffAmount = 0m;
            decimal totalTariffEnergy = 0m;
            var hourlyDetail = new List<HourlyTariffDetail>();

            foreach (var line in hourlyData)
            {
                var hourNum = line.DateTime24Hour.Hour + 1; // Convert 0-23 to 1-24
                var hourlyRate = rate.GetPriceForHour(hourNum);
                var effectiveRate = hourlyRate > 0 ? hourlyRate : rate.Price;

                if (effectiveRate <= 0) continue;

                var lineAmount = line.TimeValue * effectiveRate;
                totalTariffAmount += lineAmount;
                totalTariffEnergy += line.TimeValue;

                hourlyDetail.Add(new HourlyTariffDetail
                {
                    Timestamp = ToUtc(line.DateTime24Hour),
                    Hour = hourNum,
                    Kwh = line.TimeValue,
                    RateDkkPerKwh = effectiveRate,
                    AmountDkk = lineAmount
                });
            }

            if (totalTariffAmount == 0) continue;

            tariffLines.Add(new ExtractedTariffLine
            {
                PartyChargeTypeId = tariff.PartyChargeTypeId,
                Description = tariff.Description,
                AmountDkk = totalTariffAmount,
                EnergyKwh = totalTariffEnergy,
                AvgUnitPrice = totalTariffEnergy != 0 ? totalTariffAmount / totalTariffEnergy : 0,
                RateProvenance = provenance,
                HourlyDetail = hourlyDetail
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
