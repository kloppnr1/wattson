using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Migration.Core.Models;

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

                // Group metering points
                var mps = g
                    .GroupBy(d => d.Delpoint.Gsrn)
                    .Where(mg => !string.IsNullOrEmpty(mg.Key))
                    .Select(mg =>
                    {
                        var firstMp = mg.First();
                        var mp = new ExtractedMeteringPoint
                        {
                            Gsrn = firstMp.Delpoint.Gsrn,
                            GridArea = MapGridArea(firstMp.Delpoint.Powerexchangearea),
                            SupplyStart = firstMp.Contract.Contractstartdate,
                            SupplyEnd = firstMp.Contract.Contractenddate == NoEndDate
                                ? null
                                : firstMp.Contract.Contractenddate,
                        };

                        // Product periods from contract parts
                        mp.ProductPeriods = mg
                            .Where(d => !string.IsNullOrEmpty(d.ContractPart.Productnum))
                            .Select(d => new ExtractedProductPeriod
                            {
                                ProductName = d.ContractPart.Productnum,
                                Start = d.ContractPart.Startdate,
                                End = d.ContractPart.Enddate == NoEndDate ? null : d.ContractPart.Enddate,
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
            // Get rates from ExuRateTable
            // Generic rates (PRODUCTNUM = '') are per-kWh supplier margins
            var rates = await _db.ExuRateTables
                .Where(r => r.Dataareaid == DataAreaId
                    && r.Deliverycategory == DeliveryCategory
                    && r.Productnum == "" // Generic rates, not subscription-specific
                    && r.Rate != 0)
                .OrderBy(r => r.Startdate)
                .ToListAsync();

            // TODO: This gets ALL generic rates, not product-specific ones.
            // Need to refine: ExuProductExtendTable → Ratetype → ExuRateTable
            // For now, include all rates as a starting point.

            products.Add(new ExtractedProduct
            {
                Name = productNum,
                Rates = rates.Select(r => new ExtractedRate
                {
                    StartDate = r.Startdate,
                    RateDkkPerKwh = r.Rate,
                }).ToList()
            });
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
                    PeriodStart = values.First().Timeofvalue,
                    PeriodEnd = values.Last().Timeofvalue.AddHours(1),
                    Resolution = ts.Timeresolution == 60 ? "PT1H" : "PT15M",
                    Observations = values.Select(v => new ExtractedObservation
                    {
                        Timestamp = v.Timeofvalue,
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

    private static string MapGridArea(string powerExchangeArea)
        => powerExchangeArea?.Trim() switch
        {
            "DK1" => "DK1",
            "DK2" => "DK2",
            _ => "DK1" // Default
        };

    private static string MapQuality(int qualityOfValue)
        => qualityOfValue switch
        {
            36 => "A01",  // Measured
            56 => "A02",  // Estimated
            _ => "A01"
        };

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
