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
    logger.LogInformation("Customers: {C}, Products: {P}, MPs: {M}",
        data.Customers.Count, data.Products.Count, data.Customers.Sum(c => c.MeteringPoints.Count));

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
var pushCommand = new Command("push", "Push cached data to WattsOn API (no VPN needed)")
{
    cacheFileOption,
    wattsOnUrlOption
};

pushCommand.SetHandler(async (context) =>
{
    var cacheFile = context.ParseResult.GetValueForOption(cacheFileOption)!;
    var wattsOnUrl = context.ParseResult.GetValueForOption(wattsOnUrlOption)!;

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
            links = data.PriceLinks.Select(l => new { gsrn = l.Gsrn, chargeId = l.ChargeId, ownerGln = l.OwnerGln, effectiveDate = l.EffectiveDate }).ToList()
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
var rootCommand = new RootCommand("WattsOn Migration Tool — extract from Xellent, push to WattsOn")
{
    extractCommand,
    pushCommand,
    reportCommand,
    xellentReportCommand
};

return await rootCommand.InvokeAsync(args);
