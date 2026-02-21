using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WattsOn.Migration.Core.Models;
using WattsOn.Migration.WattsOnApi;
using WattsOn.Migration.XellentData;

// ─── Shared options ───
var cacheFileOption = new Option<string>(
    name: "--cache",
    description: "Path to the extracted data cache file (JSON)",
    getDefaultValue: () => "./cache/extracted.json");

var wattsOnUrlOption = new Option<string>(
    name: "--wattson-url",
    description: "WattsOn API base URL",
    getDefaultValue: () => "http://localhost:5100");

// ─── JSON serializer ───
var jsonOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter() }
};

// ═══════════════════════════════════════════════════════════════
// EXTRACT command — hits Xellent, saves to local cache
// ═══════════════════════════════════════════════════════════════
var accountsOption = new Option<string[]>("--accounts", "Xellent account numbers") { IsRequired = true, AllowMultipleArgumentsPerToken = true };
var xellentConnectionOption = new Option<string>("--xellent-connection", "Xellent SQL Server connection string") { IsRequired = true };
var supplierGlnOption = new Option<string>("--supplier-gln", "Supplier GLN") { IsRequired = true };
var supplierNameOption = new Option<string>("--supplier-name", description: "Supplier name", getDefaultValue: () => "Verdo");
var includeTimeSeriesOption = new Option<bool>("--include-timeseries", description: "Include time series", getDefaultValue: () => true);
var sinceOption = new Option<DateTime?>("--since", "Cutoff for time series (default: 2 years ago)");
var includeSettlementsOption = new Option<bool>("--include-settlements", description: "Include settlements", getDefaultValue: () => true);

var extractCommand = new Command("extract", "Extract data from Xellent and save to local cache (requires VPN)")
{
    accountsOption, xellentConnectionOption, supplierGlnOption, supplierNameOption,
    includeTimeSeriesOption, sinceOption, includeSettlementsOption, cacheFileOption
};

extractCommand.SetHandler(async (context) =>
{
    var accounts = context.ParseResult.GetValueForOption(accountsOption)!;
    var xellentConnection = context.ParseResult.GetValueForOption(xellentConnectionOption)!;
    var supplierGln = context.ParseResult.GetValueForOption(supplierGlnOption)!;
    var supplierName = context.ParseResult.GetValueForOption(supplierNameOption)!;
    var includeTimeSeries = context.ParseResult.GetValueForOption(includeTimeSeriesOption);
    var sinceDate = context.ParseResult.GetValueForOption(sinceOption);
    var includeSettlements = context.ParseResult.GetValueForOption(includeSettlementsOption);
    var cacheFile = context.ParseResult.GetValueForOption(cacheFileOption)!;

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning)); // Quiet EF
    services.AddDbContext<XellentDbContext>(o => o.UseSqlServer(xellentConnection, s => s.UseCompatibilityLevel(110)));
    services.AddTransient<XellentExtractionService>();
    services.AddTransient<XellentSettlementService>();
    var sp = services.BuildServiceProvider();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Extract");
    var extraction = sp.GetRequiredService<XellentExtractionService>();
    var settlementService = sp.GetRequiredService<XellentSettlementService>();

    var sw = Stopwatch.StartNew();
    logger.LogInformation("Extracting accounts: {Accounts}", string.Join(", ", accounts));

    var data = new ExtractedData
    {
        AccountNumbers = accounts,
        ExtractedAt = DateTimeOffset.UtcNow,
        SupplierGln = supplierGln,
        SupplierName = supplierName
    };

    // Customers + products
    data.Customers = await extraction.ExtractCustomersAsync(accounts);
    data.Products = await extraction.ExtractDistinctProductsAsync(accounts);

    // Identify addon products (those with descriptions starting with "Tillæg") and enrich supply product periods
    var mainProductNames = data.Products.Where(p => p.Description == null || !p.Description.StartsWith("Tillæg")).Select(p => p.Name).ToHashSet();
    var addonProductNames = data.Products.Where(p => p.Description != null && p.Description.StartsWith("Tillæg")).Select(p => p.Name).ToHashSet();
    if (addonProductNames.Count > 0)
    {
        await extraction.EnrichWithAddonProductPeriodsAsync(data.Customers, addonProductNames);
        logger.LogInformation("Enriched {Count} addon product periods: {Names}",
            addonProductNames.Count, string.Join(", ", addonProductNames));
    }

    logger.LogInformation("Customers: {C}, Products: {P} ({Main} main + {Addon} addons), MPs: {M}",
        data.Customers.Count, data.Products.Count, mainProductNames.Count, addonProductNames.Count,
        data.Customers.Sum(c => c.MeteringPoints.Count));

    var allGsrns = data.Customers.SelectMany(c => c.MeteringPoints).Select(mp => mp.Gsrn).ToList();

    // Prices + links
    data.Prices = await extraction.ExtractPricesAsync(allGsrns);
    data.PriceLinks = await extraction.ExtractPriceLinksAsync(allGsrns);
    logger.LogInformation("Prices: {P} ({Pp} points), Links: {L}",
        data.Prices.Count, data.Prices.Sum(p => p.Points.Count), data.PriceLinks.Count);

    // Time series
    if (includeTimeSeries)
    {
        var tsStart = sinceDate ?? DateTime.UtcNow.AddYears(-2);
        data.TimeSeries = await extraction.ExtractTimeSeriesAsync(allGsrns, new DateTimeOffset(tsStart, TimeSpan.Zero));
        logger.LogInformation("Time series: {T} ({O} observations)",
            data.TimeSeries.Count, data.TimeSeries.Sum(t => t.Observations.Count));
    }

    // Settlements — uses XellentSettlementService (CorrectionService-equivalent logic)
    // This captures ALL charge types: tariffs, subscriptions, fees, and product margins from ExuRateTable
    if (includeSettlements)
    {
        data.Settlements = new List<ExtractedSettlement>();
        foreach (var gsrn in allGsrns)
        {
            var settlements = await settlementService.BuildSettlementsAsync(gsrn);
            data.Settlements.AddRange(settlements);
        }
        logger.LogInformation("Settlements: {S}", data.Settlements.Count);
    }

    // Derive margin rates from billing data for products missing ExuRateTable rates.
    // This is the most accurate source — it's what was ACTUALLY invoiced.
    if (data.Settlements.Count > 0 && data.Products.Count > 0)
    {
        var productPeriods = data.Customers
            .SelectMany(c => c.MeteringPoints)
            .SelectMany(mp => mp.ProductPeriods)
            .OrderBy(pp => pp.Start)
            .ToList();

        foreach (var product in data.Products)
        {
            // Find product periods for this product
            var periods = productPeriods.Where(pp => pp.ProductName == product.Name).ToList();
            if (periods.Count == 0) continue;

            // Derive rates from settlement margin data per product period
            var derivedRates = new List<ExtractedRate>();
            foreach (var period in periods)
            {
                var periodStart = period.Start.UtcDateTime;
                var periodEnd = period.End?.UtcDateTime ?? DateTime.MaxValue;

                var periodSettlements = data.Settlements
                    .Where(s => s.PeriodStart >= periodStart && s.PeriodStart < periodEnd)
                    .ToList();

                if (periodSettlements.Count == 0) continue;

                var totalEnergy = periodSettlements.Sum(s => s.TotalEnergyKwh);
                var totalMargin = periodSettlements.Sum(s => s.MarginAmountDkk);

                if (totalEnergy > 0)
                {
                    derivedRates.Add(new ExtractedRate
                    {
                        StartDate = period.Start,
                        EndDate = period.End,
                        RateDkkPerKwh = Math.Round(totalMargin / totalEnergy, 6)
                    });
                }
            }

            var hasRealRates = product.Rates.Any(r => r.RateDkkPerKwh != 0);

            if (derivedRates.Count > 0 && !hasRealRates)
            {
                // No ExuRateTable rates (or all zero) — use billing-derived rates
                product.Rates = derivedRates;
                logger.LogInformation("Product {Name}: derived {Count} margin rates from billing data (ExuRateTable had {Old} rates, all zero)",
                    product.Name, derivedRates.Count, product.Rates.Count);
            }
            else if (derivedRates.Count > 0)
            {
                // Both exist — log comparison for verification
                logger.LogInformation("Product {Name}: {ExuCount} ExuRateTable rates, {DerivedCount} billing-derived rates",
                    product.Name, product.Rates.Count, derivedRates.Count);
            }
        }
    }

    // Save cache
    Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
    await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(data, jsonOptions));
    sw.Stop();

    logger.LogInformation("Extraction complete in {T:F1}s → {File}", sw.Elapsed.TotalSeconds, Path.GetFullPath(cacheFile));
    Console.WriteLine(data.Summary);
});

// ═══════════════════════════════════════════════════════════════
// PUSH command — reads local cache, pushes to WattsOn API
// ═══════════════════════════════════════════════════════════════
var yearsBackOption = new Option<int>(
    name: "--years-back",
    description: "Only migrate data from this many years before the supply end date (default: 3)",
    getDefaultValue: () => 3);

var pushCommand = new Command("push", "Push cached data to WattsOn API (no VPN needed)")
{
    cacheFileOption,
    wattsOnUrlOption,
    yearsBackOption
};

pushCommand.SetHandler(async (context) =>
{
    var cacheFile = context.ParseResult.GetValueForOption(cacheFileOption)!;
    var wattsOnUrl = context.ParseResult.GetValueForOption(wattsOnUrlOption)!;
    var yearsBack = context.ParseResult.GetValueForOption(yearsBackOption);

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    services.AddHttpClient<WattsOnMigrationClient>(c => c.BaseAddress = new Uri(wattsOnUrl));
    var sp = services.BuildServiceProvider();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var wattsOn = sp.GetRequiredService<WattsOnMigrationClient>();

    if (!File.Exists(cacheFile))
    {
        logger.LogError("Cache file not found: {File}. Run 'extract' first.", cacheFile);
        return;
    }

    var data = JsonSerializer.Deserialize<ExtractedData>(await File.ReadAllTextAsync(cacheFile), jsonOptions)!;
    logger.LogInformation("Loaded cache: {Summary}", data.Summary.Replace("\n", " ").Trim());

    // ─── Apply migration window: only push data from (supply end - yearsBack) ───
    // Find the latest supply end date across all customers/MPs
    var allSupplyEnds = data.Customers
        .SelectMany(c => c.MeteringPoints)
        .Select(mp => mp.SupplyEnd ?? DateTimeOffset.UtcNow)
        .ToList();
    var latestSupplyEnd = allSupplyEnds.Count > 0 ? allSupplyEnds.Max() : DateTimeOffset.UtcNow;
    var migrationCutoff = latestSupplyEnd.AddYears(-yearsBack);
    logger.LogInformation("Migration window: {YearsBack} years back from {SupplyEnd:yyyy-MM-dd} → cutoff {Cutoff:yyyy-MM-dd}",
        yearsBack, latestSupplyEnd, migrationCutoff);

    // Filter settlements
    var origSettlementCount = data.Settlements.Count;
    data.Settlements = data.Settlements
        .Where(s => s.PeriodStart >= migrationCutoff)
        .ToList();
    if (origSettlementCount != data.Settlements.Count)
        logger.LogInformation("Settlements: {Kept}/{Total} after {YearsBack}-year cutoff",
            data.Settlements.Count, origSettlementCount, yearsBack);

    // Filter product periods on customers
    foreach (var c in data.Customers)
    {
        foreach (var mp in c.MeteringPoints)
        {
            // Trim supply start to cutoff if it's earlier
            if (mp.SupplyStart < migrationCutoff)
                mp.SupplyStart = migrationCutoff;

            // Filter product periods
            mp.ProductPeriods = mp.ProductPeriods
                .Where(pp => pp.End == null || pp.End > migrationCutoff)
                .Select(pp =>
                {
                    if (pp.Start < migrationCutoff) pp.Start = migrationCutoff;
                    return pp;
                })
                .ToList();
        }
    }

    // Filter time series observations
    foreach (var ts in data.TimeSeries)
    {
        var origCount = ts.Observations.Count;
        ts.Observations = ts.Observations
            .Where(o => o.Timestamp >= migrationCutoff)
            .ToList();
        if (origCount != ts.Observations.Count)
            logger.LogInformation("Time series: {Kept}/{Total} observations after cutoff", ts.Observations.Count, origCount);
    }

    var sw = Stopwatch.StartNew();
    var result = new MigrationResult();

    // Step 1: Supplier identity
    var supplierIdentityId = await wattsOn.EnsureSupplierIdentity(data.SupplierGln, data.SupplierName);
    logger.LogInformation("Supplier identity: {Id}", supplierIdentityId);

    // Step 2: Products
    var productResult = await wattsOn.MigrateSupplierProducts(new
    {
        supplierIdentityId,
        products = data.Products.Select(p => new { p.Name, p.Description, pricingModel = p.PricingModel, isActive = true }).ToList()
    });
    result.ProductsCreated = productResult.GetProperty("created").GetInt32();
    result.ProductsSkipped = productResult.GetProperty("skipped").GetInt32();
    var productIdMap = new Dictionary<string, Guid>();
    foreach (var kv in productResult.GetProperty("products").EnumerateObject())
        productIdMap[kv.Name] = kv.Value.GetGuid();

    // Step 3: Customers
    var customerPayload = new
    {
        supplierIdentityId,
        customers = data.Customers.Select(c => new
        {
            name = c.Name, cpr = c.Cpr, cvr = c.Cvr, email = c.Email, phone = c.Phone,
            meteringPoints = c.MeteringPoints.Select(mp => new
            {
                gsrn = mp.Gsrn, type = "Forbrug", art = "Fysisk", settlementMethod = "Flex",
                gridArea = mp.GridArea, gridOperatorGln = mp.GridOperatorGln,
                supplyStart = mp.SupplyStart, supplyEnd = mp.SupplyEnd
            }).ToList()
        }).ToList()
    };
    var customerResult = await wattsOn.MigrateCustomers(customerPayload);
    result.CustomersCreated = customerResult.GetProperty("customersCreated").GetInt32();
    result.CustomersSkipped = customerResult.GetProperty("skipped").GetInt32();
    result.MeteringPointsCreated = customerResult.GetProperty("meteringPointsCreated").GetInt32();
    result.SuppliesCreated = customerResult.GetProperty("suppliesCreated").GetInt32();

    // Step 4: Product periods
    var allPeriods = data.Customers.SelectMany(c => c.MeteringPoints)
        .SelectMany(mp => mp.ProductPeriods.Select(pp => new
        { gsrn = mp.Gsrn, productName = pp.ProductName, productStart = pp.Start, productEnd = pp.End })).ToList();
    if (allPeriods.Count > 0)
    {
        var periodResult = await wattsOn.MigrateSupplyProductPeriods(new { supplierIdentityId, periods = allPeriods });
        result.ProductPeriodsCreated = periodResult.GetProperty("created").GetInt32();
        result.ProductPeriodsSkipped = periodResult.GetProperty("skipped").GetInt32();
    }

    // Step 5: Margins
    foreach (var product in data.Products.Where(p => p.Rates.Count > 0))
    {
        if (!productIdMap.TryGetValue(product.Name, out var productId)) continue;
        var marginResult = await wattsOn.MigrateSupplierMargins(new
        {
            supplierProductId = productId,
            rates = product.Rates.Select(r => new { validFrom = r.StartDate, priceDkkPerKwh = r.RateDkkPerKwh }).ToList()
        });
        result.MarginsCreated += marginResult.GetProperty("inserted").GetInt32();
    }

    // Step 6: Prices
    if (data.Prices.Count > 0)
    {
        var priceResult = await wattsOn.MigratePrices(new
        {
            prices = data.Prices.Select(p => new
            {
                chargeId = p.ChargeId, ownerGln = p.OwnerGln, type = p.Type, description = p.Description,
                effectiveDate = p.EffectiveDate, resolution = p.Resolution, isTax = p.IsTax,
                isPassThrough = p.IsPassThrough, category = p.Category,
                points = p.Points.Select(pt => new { timestamp = pt.Timestamp, price = pt.Price }).ToList()
            }).ToList()
        });
        result.PricesCreated = priceResult.GetProperty("created").GetInt32();
        result.PricesUpdated = priceResult.GetProperty("updated").GetInt32();
        result.PricePointsCreated = priceResult.GetProperty("pointsCreated").GetInt32();
    }

    // Step 7: Price links
    if (data.PriceLinks.Count > 0)
    {
        var linkResult = await wattsOn.MigratePriceLinks(new
        {
            links = data.PriceLinks.Select(l => new { gsrn = l.Gsrn, chargeId = l.ChargeId, chargeTypeCode = l.ChargeTypeCode, ownerGln = l.OwnerGln, effectiveDate = l.EffectiveDate, endDate = l.EndDate }).ToList()
        });
        result.PriceLinksCreated = linkResult.GetProperty("created").GetInt32();
        result.PriceLinksSkipped = linkResult.GetProperty("skipped").GetInt32();
    }

    // Step 8: Time series
    if (data.TimeSeries.Count > 0)
    {
        var tsResult = await wattsOn.MigrateTimeSeries(new
        {
            timeSeries = data.TimeSeries.Select(ts => new
            {
                gsrn = ts.Gsrn, periodStart = ts.PeriodStart, periodEnd = ts.PeriodEnd, resolution = ts.Resolution,
                observations = ts.Observations.Select(o => new { timestamp = o.Timestamp, kwh = o.Kwh, quality = o.Quality }).ToList()
            }).ToList()
        });
        result.TimeSeriesCreated = tsResult.GetProperty("seriesCreated").GetInt32();
        result.ObservationsCreated = tsResult.GetProperty("observationsCreated").GetInt32();
        result.TimeSeriesSkipped = tsResult.GetProperty("skipped").GetInt32();
    }

    // Step 9: Settlements
    if (data.Settlements.Count > 0)
    {
        var settlementResult = await wattsOn.MigrateSettlements(new
        {
            settlements = data.Settlements.Select(s => new
            {
                gsrn = s.Gsrn, periodStart = s.PeriodStart, periodEnd = s.PeriodEnd,
                billingLogNum = s.BillingLogNum, externalInvoiceReference = s.HistKeyNumber,
                totalEnergyKwh = s.TotalEnergyKwh, spotAmountDkk = s.SpotAmountDkk, marginAmountDkk = s.MarginAmountDkk,
                tariffLines = s.TariffLines.Select(t => new
                { chargeId = t.PartyChargeTypeId, description = t.Description, energyKwh = t.EnergyKwh, avgUnitPrice = t.AvgUnitPrice, amountDkk = t.AmountDkk, isSubscription = t.IsSubscription }).ToList()
            }).ToList()
        });
        result.SettlementsCreated = settlementResult.GetProperty("created").GetInt32();
        result.SettlementsSkipped = settlementResult.GetProperty("skipped").GetInt32();
    }

    sw.Stop();
    result.Duration = sw.Elapsed;
    Console.WriteLine(result.ToString());
});

// ═══════════════════════════════════════════════════════════════
// REPORT command — produces verification report from cache
// ═══════════════════════════════════════════════════════════════
var reportCommand = new Command("report", "Generate verification report from cached data")
{
    cacheFileOption,
    new Option<string>("--output", description: "Output directory for reports", getDefaultValue: () => "./cache/reports")
};

reportCommand.SetHandler(async (context) =>
{
    var cacheFile = context.ParseResult.GetValueForOption(cacheFileOption)!;
    var outputDir = context.ParseResult.GetValueForOption(reportCommand.Options.OfType<Option<string>>().First(o => o.Name == "output"))!;

    if (!File.Exists(cacheFile))
    {
        Console.Error.WriteLine($"Cache file not found: {cacheFile}. Run 'extract' first.");
        return;
    }

    var data = JsonSerializer.Deserialize<ExtractedData>(await File.ReadAllTextAsync(cacheFile), jsonOptions)!;
    Directory.CreateDirectory(outputDir);

    // Generate interactive HTML report
    var html = WattsOn.Migration.Cli.HtmlReportGenerator.Generate(data);
    var htmlPath = Path.Combine(outputDir, $"provenance-{data.AccountNumbers.FirstOrDefault() ?? "unknown"}.html");
    await File.WriteAllTextAsync(htmlPath, html);

    Console.WriteLine($"Report generated: {Path.GetFullPath(htmlPath)}");
});

// ═══════════════════════════════════════════════════════════════
// XELLENT-REPORT command — builds settlements using CorrectionService logic
// ═══════════════════════════════════════════════════════════════
var xrAccountsOption = new Option<string[]>("--accounts", "Xellent account numbers") { IsRequired = true, AllowMultipleArgumentsPerToken = true };
var xrConnectionOption = new Option<string>("--xellent-connection", "Xellent SQL Server connection string") { IsRequired = true };
var xrOutputOption = new Option<string>("--output", description: "Output directory", getDefaultValue: () => "./cache/reports");

var xellentReportCommand = new Command("xellent-report", "Build settlement provenance using CorrectionService logic (reference implementation)")
{
    xrAccountsOption, xrConnectionOption, xrOutputOption
};

xellentReportCommand.SetHandler(async (context) =>
{
    var accounts = context.ParseResult.GetValueForOption(xrAccountsOption)!;
    var connection = context.ParseResult.GetValueForOption(xrConnectionOption)!;
    var outputDir = context.ParseResult.GetValueForOption(xrOutputOption)!;

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
    services.AddDbContext<XellentDbContext>(o => o.UseSqlServer(connection, s => s.UseCompatibilityLevel(110)));
    services.AddTransient<XellentExtractionService>();
    services.AddTransient<XellentSettlementService>();
    var sp = services.BuildServiceProvider();
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("XellentReport");
    var extraction = sp.GetRequiredService<XellentExtractionService>();
    var settlementService = sp.GetRequiredService<XellentSettlementService>();

    var sw = Stopwatch.StartNew();

    // Extract customers to get metering point IDs
    var customers = await extraction.ExtractCustomersAsync(accounts);
    var allGsrns = customers.SelectMany(c => c.MeteringPoints).Select(mp => mp.Gsrn).ToList();
    logger.LogInformation("Found {Count} metering points for accounts {Accounts}", allGsrns.Count, string.Join(", ", accounts));

    // Extract prices for reference
    var prices = await extraction.ExtractPricesAsync(allGsrns);
    var products = await extraction.ExtractDistinctProductsAsync(accounts);

    // Build settlements using CorrectionService-equivalent logic
    var allSettlements = new List<ExtractedSettlement>();
    foreach (var gsrn in allGsrns)
    {
        var settlements = await settlementService.BuildSettlementsAsync(gsrn);
        allSettlements.AddRange(settlements);
    }

    sw.Stop();
    logger.LogInformation("Built {Count} settlements in {T:F1}s", allSettlements.Count, sw.Elapsed.TotalSeconds);

    // Build ExtractedData for the HTML generator
    var data = new ExtractedData
    {
        AccountNumbers = accounts,
        ExtractedAt = DateTimeOffset.UtcNow,
        SupplierGln = "5790001330552",
        SupplierName = "Verdo (CorrectionService reference)",
        Customers = customers,
        Products = products,
        Prices = prices,
        PriceLinks = new(),
        TimeSeries = new(),
        Settlements = allSettlements
    };

    Directory.CreateDirectory(outputDir);
    var html = WattsOn.Migration.Cli.HtmlReportGenerator.Generate(data);
    var htmlPath = Path.Combine(outputDir, $"xellent-provenance-{accounts.FirstOrDefault() ?? "unknown"}.html");
    await File.WriteAllTextAsync(htmlPath, html);

    // Also save the raw data as JSON cache
    var cachePath = Path.Combine(outputDir, $"xellent-{accounts.FirstOrDefault() ?? "unknown"}.json");
    await File.WriteAllTextAsync(cachePath, JsonSerializer.Serialize(data, jsonOptions));

    Console.WriteLine($"XellentSettlement reference report: {Path.GetFullPath(htmlPath)}");
    Console.WriteLine($"Raw data cache: {Path.GetFullPath(cachePath)}");
    Console.WriteLine($"{allSettlements.Count} settlements, {sw.Elapsed.TotalSeconds:F1}s");
});

// ═══════════════════════════════════════════════════════════════
// ROOT command (backward compat: direct migration still works)
// ═══════════════════════════════════════════════════════════════
// ═══════════════════════════════════════════════════════════════
// INVESTIGATE command — raw Xellent DB queries for debugging
// ═══════════════════════════════════════════════════════════════
var invProductOption = new Option<string>("--product", "Product number to investigate") { IsRequired = true };
var invConnectionOption = new Option<string>("--xellent-connection", "Xellent SQL Server connection string") { IsRequired = true };

var investigateCommand = new Command("investigate", "Investigate product rate structure in Xellent DB")
{
    invProductOption, invConnectionOption
};

investigateCommand.SetHandler(async (context) =>
{
    var productNum = context.ParseResult.GetValueForOption(invProductOption)!;
    var connection = context.ParseResult.GetValueForOption(invConnectionOption)!;

    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddDbContext<XellentDbContext>(o => o.UseSqlServer(connection, s => s.UseCompatibilityLevel(110)));
    var sp = services.BuildServiceProvider();
    var db = sp.GetRequiredService<XellentDbContext>();

    Console.WriteLine($"═══ Investigating product: {productNum} ═══\n");

    // 1. InventTable
    var inv = await db.InventTables.FirstOrDefaultAsync(i => i.DataAreaId == "hol" && i.ItemId == productNum);
    if (inv != null)
        Console.WriteLine($"InventTable: ItemId={inv.ItemId}, ItemType={inv.ItemType}, UseRateFromFlexPricing={inv.ExuUseRateFromFlexPricing}");
    else
        Console.WriteLine($"InventTable: NOT FOUND for ItemId={productNum}");

    // 2. All ExuProductExtendTable entries
    var extends_ = await db.ExuProductExtendTables
        .Where(pe => pe.DataAreaId == "hol" && pe.Productnum == productNum)
        .OrderBy(pe => pe.Startdate)
        .ToListAsync();
    Console.WriteLine($"\nExuProductExtendTable ({extends_.Count} entries):");
    foreach (var pe in extends_)
        Console.WriteLine($"  Producttype={pe.Producttype,-25} Start={pe.Startdate:yyyy-MM-dd}  End={pe.Enddate:yyyy-MM-dd}");

    // 3. For each product type, check InventTable and RateTable
    var productTypes = extends_.Select(pe => pe.Producttype).Distinct().ToList();
    foreach (var pt in productTypes)
    {
        var ptInv = await db.InventTables.FirstOrDefaultAsync(i => i.DataAreaId == "hol" && i.ItemId == pt);
        Console.WriteLine($"\n─── Product type: {pt} ───");
        if (ptInv != null)
            Console.WriteLine($"  InventTable: ItemType={ptInv.ItemType}, UseRateFromFlexPricing={ptInv.ExuUseRateFromFlexPricing}");
        else
            Console.WriteLine($"  InventTable: NOT FOUND");

        // Generic rates (Productnum = "")
        var genericRates = await db.ExuRateTables
            .Where(r => r.Dataareaid == "hol" && r.Ratetype == pt && r.Deliverycategory == "El-ekstern" && r.Productnum == "")
            .OrderBy(r => r.Startdate)
            .ToListAsync();
        Console.WriteLine($"  ExuRateTable (generic, Productnum=''): {genericRates.Count} entries");
        foreach (var r in genericRates.TakeLast(5))
            Console.WriteLine($"    Start={r.Startdate:yyyy-MM-dd}  Rate={r.Rate}  CompanyId={r.Companyid}");
        if (genericRates.Count > 5) Console.WriteLine($"    ... ({genericRates.Count - 5} earlier entries)");

        // Product-specific rates (Productnum = productNum)
        var specificRates = await db.ExuRateTables
            .Where(r => r.Dataareaid == "hol" && r.Ratetype == pt && r.Deliverycategory == "El-ekstern" && r.Productnum == productNum)
            .OrderBy(r => r.Startdate)
            .ToListAsync();
        Console.WriteLine($"  ExuRateTable (product-specific, Productnum='{productNum}'): {specificRates.Count} entries");
        foreach (var r in specificRates.TakeLast(5))
            Console.WriteLine($"    Start={r.Startdate:yyyy-MM-dd}  Rate={r.Rate}  CompanyId={r.Companyid}");
        if (specificRates.Count > 5) Console.WriteLine($"    ... ({specificRates.Count - 5} earlier entries)");

        // Any delivery category
        var allCatRates = await db.ExuRateTables
            .Where(r => r.Dataareaid == "hol" && r.Ratetype == pt && r.Productnum == "")
            .Select(r => r.Deliverycategory)
            .Distinct()
            .ToListAsync();
        Console.WriteLine($"  Delivery categories with generic rates: [{string.Join(", ", allCatRates)}]");
    }

    // 4. Check RateTable for the product itself as Ratetype
    var selfRates = await db.ExuRateTables
        .Where(r => r.Dataareaid == "hol" && r.Ratetype == productNum && r.Deliverycategory == "El-ekstern")
        .OrderBy(r => r.Startdate)
        .ToListAsync();
    Console.WriteLine($"\n─── RateTable where Ratetype='{productNum}' ───");
    Console.WriteLine($"  {selfRates.Count} entries");
    foreach (var r in selfRates.TakeLast(5))
        Console.WriteLine($"    Start={r.Startdate:yyyy-MM-dd}  Rate={r.Rate}  Productnum={r.Productnum}  CompanyId={r.Companyid}");

    // 5. Check ALL RateTable entries mentioning this product in ANY column
    var anyMention = await db.ExuRateTables
        .Where(r => r.Dataareaid == "hol" && (r.Productnum == productNum || r.Ratetype == productNum))
        .CountAsync();
    Console.WriteLine($"\n  Total RateTable rows mentioning '{productNum}' in any column: {anyMention}");
});

// ═══════════════════════════════════════════════════════════════
// SCHEMA command — dump actual DB table columns for investigation
// ═══════════════════════════════════════════════════════════════
var schemaCommand = new Command("schema", "Dump table columns from Xellent DB")
{
    xellentConnectionOption
};
schemaCommand.SetHandler(async (context) =>
{
    var connStr = context.ParseResult.GetValueForOption(xellentConnectionOption)!;
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
    services.AddDbContext<XellentDbContext>(o => o.UseSqlServer(connStr, s => s.UseCompatibilityLevel(110)));
    var sp = services.BuildServiceProvider();
    var db = sp.GetRequiredService<XellentDbContext>();

    var tables = new[] { "EXU_PRICEELEMENTTABLE", "EXU_PRICEELEMENTRATES", "EXU_PRICEELEMENTCHECKDATA", "EXU_PRICEELEMENTCHECK" };
    foreach (var table in tables)
    {
        Console.WriteLine($"\n═══ {table} ═══");
        var cols = await db.Database.SqlQueryRaw<SchemaRow>(
            $"SELECT COLUMN_NAME as Name, DATA_TYPE as DataType FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' ORDER BY ORDINAL_POSITION")
            .ToListAsync();
        foreach (var c in cols)
            Console.WriteLine($"  {c.Name,-40} {c.DataType}");
    }

    // Also check CD tariff rate rows to see what differentiates them
    Console.WriteLine("\n═══ CD tariff rate sample (2024-01, first 10 rows) ═══");
    var cdRates = await db.Database.SqlQueryRaw<CdRateSample>(
        @"SELECT TOP 10 RECID as RecId, STARTDATE as StartDate, PRICE as Price1, PRICE2_ as Price2, PRICE24_ as Price24
          FROM EXU_PRICEELEMENTRATES
          WHERE DATAAREAID = 'ER' AND PARTYCHARGETYPEID = 'CD' AND CHARGETYPECODE = 3
            AND STARTDATE >= '2024-01-01' AND STARTDATE < '2024-04-01'
          ORDER BY STARTDATE, RECID")
        .ToListAsync();
    foreach (var r in cdRates)
        Console.WriteLine($"  RecId={r.RecId} Start={r.StartDate:yyyy-MM-dd} P1={r.Price1:F6} P2={r.Price2:F6} P24={r.Price24:F6}");

    // Check PriceElementTable entries for CD
    Console.WriteLine("\n═══ PriceElementTable rows for 'CD' ═══");
    var cdTables = await db.PriceElementTables
        .Where(t => t.DataAreaId == "ER" && t.PartyChargeTypeId == "CD")
        .ToListAsync();
    foreach (var t in cdTables)
        Console.WriteLine($"  RecId={t.RecId} ChargeType={t.ChargeTypeCode} Desc='{t.Description}'");
});

var rootCommand = new RootCommand("WattsOn Migration Tool — extract from Xellent, push to WattsOn")
{
    extractCommand,
    pushCommand,
    reportCommand,
    xellentReportCommand,
    investigateCommand,
    schemaCommand
};

return await rootCommand.InvokeAsync(args);

// Helper types for schema query
public record SchemaRow { public string Name { get; init; } = ""; public string DataType { get; init; } = ""; }
public record CdRateSample { public long RecId { get; init; } public DateTime StartDate { get; init; } public decimal Price1 { get; init; } public decimal Price2 { get; init; } public decimal Price24 { get; init; } }
