using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Migration.XellentData.Entities;

namespace WattsOn.Migration.XellentData;

/// <summary>
/// Read-only diagnostic service for auditing Xellent migration data quality.
/// Does NOT modify any data — all methods are pure queries.
/// </summary>
public class XellentAuditService
{
    private readonly XellentDbContext _db;
    private readonly ILogger<XellentAuditService> _logger;
    private readonly string DataAreaId;
    private readonly string[] CompanyIds;
    private readonly string DeliveryCategory;
    private static readonly DateTime NoEndDate = new(1900, 1, 1);
    private static readonly TimeZoneInfo DanishTz = TimeZoneInfo.FindSystemTimeZoneById("Central European Standard Time");

    public XellentAuditService(XellentDbContext db, ILogger<XellentAuditService> logger, XellentConfig config)
    {
        _db = db;
        _logger = logger;
        DataAreaId = config.DataAreaId;
        CompanyIds = config.CompanyIds;
        DeliveryCategory = config.DeliveryCategory;
    }

    // ═══════════════════════════════════════════════════════════════
    // 5a. Rate Column Audit: Compare Rate vs Accountrate
    // ═══════════════════════════════════════════════════════════════

    public async Task<RateColumnAudit> AuditRateColumnsAsync(string[] accountNumbers)
    {
        // Get all distinct product numbers from contract parts (same as ExtractDistinctProductsAsync)
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
                && CompanyIds.Contains(contract.Companyid)
                && contractPart.Productnum != ""
            select contractPart.Productnum
        ).Distinct().ToListAsync();

        var differences = new List<RateColumnDifference>();
        var totalRows = 0;

        foreach (var productNum in productNums)
        {
            var productExtend = await _db.ExuProductExtendTables
                .Where(pe => pe.DataAreaId == DataAreaId && pe.Productnum == productNum)
                .OrderByDescending(pe => pe.Startdate)
                .FirstOrDefaultAsync();

            if (productExtend == null) continue;

            // Get ALL rate rows for this product's rate type (same 4-tier fallback)
            var rates = await ResolveRatesAsync(productNum, productExtend.Producttype);
            totalRows += rates.Count;

            foreach (var rate in rates)
            {
                if (rate.Rate != rate.Accountrate)
                {
                    differences.Add(new RateColumnDifference
                    {
                        Productnum = productNum,
                        Ratetype = rate.Ratetype,
                        Startdate = rate.Startdate,
                        Rate = rate.Rate,
                        Accountrate = rate.Accountrate,
                        Recid = rate.Recid
                    });
                }
            }
        }

        // Also check addon product rate types (e.g. "Grøn strøm")
        var addonRateTypes = await GetAddonRateTypesAsync(accountNumbers);
        foreach (var rateType in addonRateTypes)
        {
            var rates = await _db.ExuRateTables
                .Where(r => r.Dataareaid == DataAreaId
                    && CompanyIds.Contains(r.Companyid)
                    && r.Deliverycategory == DeliveryCategory
                    && r.Ratetype == rateType
                    && r.Productnum == "")
                .OrderBy(r => r.Startdate)
                .ToListAsync();

            totalRows += rates.Count;

            foreach (var rate in rates)
            {
                if (rate.Rate != rate.Accountrate)
                {
                    differences.Add(new RateColumnDifference
                    {
                        Productnum = "(addon)",
                        Ratetype = rate.Ratetype,
                        Startdate = rate.Startdate,
                        Rate = rate.Rate,
                        Accountrate = rate.Accountrate,
                        Recid = rate.Recid
                    });
                }
            }
        }

        return new RateColumnAudit
        {
            TotalRateRows = totalRows,
            RowsWhereAccountRateDiffers = differences.Count,
            Differences = differences
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 5b. Rate Accuracy Audit: Extracted rate vs actual billing
    // ═══════════════════════════════════════════════════════════════

    public async Task<RateAccuracyAudit> AuditRateAccuracyAsync(string[] accountNumbers)
    {
        // Get metering points
        var customers = await GetMeteringPointsAsync(accountNumbers);
        var mismatches = new List<RateMismatch>();
        var periodsChecked = 0;
        var periodsMatch = 0;

        foreach (var (gsrn, xellentMp) in customers)
        {
            // Search by GSRN and Xellent metering point (FlexBillingHistory may use either)
            var searchIds = new List<string> { gsrn };
            if (!string.IsNullOrEmpty(xellentMp) && xellentMp != gsrn)
                searchIds.Add(xellentMp);

            // Load billing periods
            var periods = await _db.FlexBillingHistoryTables
                .Where(h => h.DataAreaId == DataAreaId
                    && searchIds.Contains(h.MeteringPoint)
                    && h.DeliveryCategory == DeliveryCategory)
                .OrderBy(h => h.HistKeyNumber)
                .ToListAsync();

            if (periods.Count == 0) continue;

            // Load all hourly lines
            var histKeys = periods.Select(p => p.HistKeyNumber).ToHashSet();
            var allLines = await _db.FlexBillingHistoryLines
                .Where(l => l.DataAreaId == DataAreaId && histKeys.Contains(l.HistKeyNumber))
                .ToListAsync();
            var linesByPeriod = allLines.GroupBy(l => l.HistKeyNumber).ToDictionary(g => g.Key, g => g.ToList());

            // Load product type chain for this metering point
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
                select new { cp.Productnum, pe.Producttype, cp.Startdate, cp.Enddate }
            ).ToListAsync();

            // Load product rates
            var typeNames = productTypes.Select(pt => pt.Producttype).Distinct().ToList();
            var productRates = typeNames.Count > 0
                ? await _db.ExuRateTables
                    .Where(r => r.Dataareaid == DataAreaId
                        && CompanyIds.Contains(r.Companyid)
                        && typeNames.Contains(r.Ratetype)
                        && r.Deliverycategory == DeliveryCategory
                        && r.Productnum == "")
                    .OrderByDescending(r => r.Startdate)
                    .ToListAsync()
                : new List<ExuRateTable>();

            var ratesByType = productRates
                .GroupBy(r => r.Ratetype)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Startdate).ToList());

            foreach (var period in periods)
            {
                if (!linesByPeriod.TryGetValue(period.HistKeyNumber, out var hourlyLines) || hourlyLines.Count == 0)
                    continue;

                periodsChecked++;

                var periodStart = period.ReqStartDate != NoEndDate && period.ReqStartDate.Year > 1901
                    ? period.ReqStartDate
                    : hourlyLines.Min(l => l.DateTime24Hour);

                // Compute billed margin from hourly data
                var totalEnergy = hourlyLines.Sum(l => l.TimeValue);
                var totalBilledMargin = hourlyLines.Sum(l => l.TimeValue * (l.CalculatedPrice - l.PowerExchangePrice));
                var avgBilledMargin = totalEnergy > 0 ? totalBilledMargin / totalEnergy : 0;

                // Find extracted rate for this period
                var activeProduct = productTypes
                    .Where(pt => pt.Startdate <= periodStart
                        && (pt.Enddate >= periodStart || pt.Enddate == NoEndDate))
                    .FirstOrDefault();

                if (activeProduct == null)
                {
                    if (Math.Abs(avgBilledMargin) > 0.001m)
                    {
                        mismatches.Add(new RateMismatch
                        {
                            Gsrn = gsrn,
                            HistKeyNumber = period.HistKeyNumber,
                            PeriodStart = periodStart,
                            ProductNum = "(none found)",
                            ExtractedRate = 0,
                            AvgBilledMargin = Math.Round(avgBilledMargin, 6),
                            MaxHourlyDeviation = Math.Abs(avgBilledMargin),
                            FallbackTierUsed = "no product type chain"
                        });
                    }
                    continue;
                }

                decimal extractedRate = 0;
                var fallbackTier = "not found";

                if (ratesByType.TryGetValue(activeProduct.Producttype, out var rates))
                {
                    var rate = rates.FirstOrDefault(r => r.Startdate <= periodStart.Date);
                    if (rate != null)
                    {
                        extractedRate = rate.Rate;
                        fallbackTier = "generic";
                    }
                }

                var deviation = Math.Abs(extractedRate - avgBilledMargin);
                if (deviation <= 0.001m)
                {
                    periodsMatch++;
                }
                else
                {
                    // Compute max hourly deviation
                    var maxHourly = hourlyLines
                        .Where(l => l.TimeValue > 0)
                        .Select(l => Math.Abs((l.CalculatedPrice - l.PowerExchangePrice) - extractedRate))
                        .DefaultIfEmpty(0)
                        .Max();

                    mismatches.Add(new RateMismatch
                    {
                        Gsrn = gsrn,
                        HistKeyNumber = period.HistKeyNumber,
                        PeriodStart = periodStart,
                        ProductNum = activeProduct.Productnum,
                        ExtractedRate = extractedRate,
                        AvgBilledMargin = Math.Round(avgBilledMargin, 6),
                        MaxHourlyDeviation = Math.Round(maxHourly, 6),
                        FallbackTierUsed = fallbackTier
                    });
                }
            }
        }

        return new RateAccuracyAudit
        {
            PeriodsChecked = periodsChecked,
            PeriodsMatch = periodsMatch,
            PeriodsMismatch = mismatches.Count,
            Mismatches = mismatches
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // 5c. Product Table Audit: EXU_PRODUCTTABLE coverage
    // ═══════════════════════════════════════════════════════════════

    public async Task<ProductTableAudit> AuditProductTableAsync(string[] accountNumbers)
    {
        // Products found via the extraction chain (ContractPart → ProductExtend → InventTable)
        var extractionProducts = await (
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
                && CompanyIds.Contains(contract.Companyid)
                && contractPart.Productnum != ""
            select contractPart.Productnum
        ).Distinct().ToListAsync();

        // Products in EXU_PRODUCTTABLE — use raw SQL since we don't have an entity
        var productTableProducts = new List<string>();
        try
        {
            var companyPlaceholders = string.Join(", ", CompanyIds.Select((_, i) => $"{{{i + 2}}}"));
            var sql = $"SELECT PRODUCTNUM as Productnum FROM EXU_PRODUCTTABLE WHERE DATAAREAID = {{0}} AND DELIVERYCATEGORY = {{1}} AND COMPANYID IN ({companyPlaceholders})";
            var parameters = new object[] { DataAreaId, DeliveryCategory }.Concat(CompanyIds.Cast<object>()).ToArray();
            var rawProducts = await _db.Database
                .SqlQueryRaw<ProductTableRow>(sql, parameters)
                .ToListAsync();
            productTableProducts = rawProducts.Select(r => r.Productnum).Distinct().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Could not query EXU_PRODUCTTABLE: {Message}", ex.Message);
            return new ProductTableAudit
            {
                ExtractionChainProducts = extractionProducts,
                ProductTableProducts = new List<string>(),
                MissingFromExtraction = new List<string>(),
                MissingFromProductTable = extractionProducts,
                Error = ex.Message
            };
        }

        var missingFromExtraction = productTableProducts.Except(extractionProducts).ToList();
        var missingFromProductTable = extractionProducts.Except(productTableProducts).ToList();

        return new ProductTableAudit
        {
            ExtractionChainProducts = extractionProducts,
            ProductTableProducts = productTableProducts,
            MissingFromExtraction = missingFromExtraction,
            MissingFromProductTable = missingFromProductTable
        };
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolve rate rows using the same 4-tier fallback as XellentExtractionService.
    /// </summary>
    private async Task<List<ExuRateTable>> ResolveRatesAsync(string productNum, string productType)
    {
        // Try 1: Generic rates
        var rates = await _db.ExuRateTables
            .Where(r => r.Dataareaid == DataAreaId && CompanyIds.Contains(r.Companyid)
                && r.Deliverycategory == DeliveryCategory
                && r.Ratetype == productType && r.Productnum == "")
            .OrderBy(r => r.Startdate).ToListAsync();

        if (rates.Count > 0 && rates.Any(r => r.Rate != 0)) return rates;

        // Try 2: Product-specific
        var specific = await _db.ExuRateTables
            .Where(r => r.Dataareaid == DataAreaId && CompanyIds.Contains(r.Companyid)
                && r.Deliverycategory == DeliveryCategory
                && r.Ratetype == productType && r.Productnum == productNum)
            .OrderBy(r => r.Startdate).ToListAsync();

        if (specific.Count > 0 && specific.Any(r => r.Rate != 0)) return specific;

        // Try 3: Self-referencing
        var self = await _db.ExuRateTables
            .Where(r => r.Dataareaid == DataAreaId && CompanyIds.Contains(r.Companyid)
                && r.Deliverycategory == DeliveryCategory
                && r.Ratetype == productNum && r.Productnum == productNum)
            .OrderBy(r => r.Startdate).ToListAsync();

        if (self.Count > 0 && self.Any(r => r.Rate != 0)) return self;

        // Try 4: Any rates for the type
        var any = await _db.ExuRateTables
            .Where(r => r.Dataareaid == DataAreaId && CompanyIds.Contains(r.Companyid)
                && r.Deliverycategory == DeliveryCategory
                && r.Ratetype == productType)
            .OrderBy(r => r.Startdate).ToListAsync();

        return any;
    }

    /// <summary>
    /// Get addon product rate type names (e.g. "Grøn strøm") from the full chain.
    /// </summary>
    private async Task<List<string>> GetAddonRateTypesAsync(string[] accountNumbers)
    {
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
                && CompanyIds.Contains(contract.Companyid)
                && inv.ItemType == 2
                && inv.ExuUseRateFromFlexPricing == 0
            select pe.Producttype
        ).Distinct().ToListAsync();

        // Get primary product rate types to exclude
        var primaryProductNums = await (
            from cust in _db.CustTables
            join contract in _db.ExuContractTables
                on new { cust.Accountnum, cust.Dataareaid }
                equals new { Accountnum = contract.Custaccount, contract.Dataareaid }
            join cp in _db.ExuContractPartTables
                on new { contract.Contractnum, contract.Dataareaid }
                equals new { cp.Contractnum, cp.Dataareaid }
            where cust.Dataareaid == DataAreaId
                && accountNumbers.Contains(cust.Accountnum)
                && contract.Deliverycategory == DeliveryCategory
                && CompanyIds.Contains(contract.Companyid)
                && cp.Productnum != ""
            select cp.Productnum
        ).Distinct().ToListAsync();

        var primaryRateTypes = new HashSet<string>();
        foreach (var pn in primaryProductNums)
        {
            var pe = await _db.ExuProductExtendTables
                .Where(p => p.DataAreaId == DataAreaId && p.Productnum == pn)
                .OrderByDescending(p => p.Startdate)
                .FirstOrDefaultAsync();
            if (pe != null) primaryRateTypes.Add(pe.Producttype);
        }

        return allProductTypes
            .Where(pt => !primaryProductNums.Contains(pt) && !primaryRateTypes.Contains(pt))
            .ToList();
    }

    private async Task<List<(string Gsrn, string? XellentMp)>> GetMeteringPointsAsync(string[] accountNumbers)
    {
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
                && CompanyIds.Contains(contract.Companyid)
                && delpoint.Deliverycategory == DeliveryCategory
            select new { Gsrn = delpoint.Gsrn ?? delpoint.Meteringpoint, XellentMp = delpoint.Meteringpoint }
        ).Distinct().ToListAsync();

        return data.Select(d => (d.Gsrn.Trim(), (string?)d.XellentMp?.Trim())).ToList();
    }
}

// ═══════════════════════════════════════════════════════════════
// Result records
// ═══════════════════════════════════════════════════════════════

public record RateColumnAudit
{
    public int TotalRateRows { get; init; }
    public int RowsWhereAccountRateDiffers { get; init; }
    public List<RateColumnDifference> Differences { get; init; } = new();
}

public record RateColumnDifference
{
    public string Productnum { get; init; } = null!;
    public string Ratetype { get; init; } = null!;
    public DateTime Startdate { get; init; }
    public decimal Rate { get; init; }
    public decimal Accountrate { get; init; }
    public long Recid { get; init; }
}

public record RateAccuracyAudit
{
    public int PeriodsChecked { get; init; }
    public int PeriodsMatch { get; init; }
    public int PeriodsMismatch { get; init; }
    public List<RateMismatch> Mismatches { get; init; } = new();
}

public record RateMismatch
{
    public string Gsrn { get; init; } = null!;
    public string HistKeyNumber { get; init; } = null!;
    public DateTime PeriodStart { get; init; }
    public string ProductNum { get; init; } = null!;
    public decimal ExtractedRate { get; init; }
    public decimal AvgBilledMargin { get; init; }
    public decimal MaxHourlyDeviation { get; init; }
    public string FallbackTierUsed { get; init; } = null!;
}

public record ProductTableAudit
{
    public List<string> ExtractionChainProducts { get; init; } = new();
    public List<string> ProductTableProducts { get; init; } = new();
    public List<string> MissingFromExtraction { get; init; } = new();
    public List<string> MissingFromProductTable { get; init; } = new();
    public string? Error { get; init; }
}

// Helper for raw SQL projection
public record ProductTableRow
{
    public string Productnum { get; init; } = "";
}
