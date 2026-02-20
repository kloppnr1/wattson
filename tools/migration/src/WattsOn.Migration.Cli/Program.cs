using System.CommandLine;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WattsOn.Migration.Core.Models;
using WattsOn.Migration.WattsOnApi;
using WattsOn.Migration.XellentData;

var accountsOption = new Option<string[]>(
    name: "--accounts",
    description: "Xellent account numbers to migrate")
{ IsRequired = true, AllowMultipleArgumentsPerToken = true };

var xellentConnectionOption = new Option<string>(
    name: "--xellent-connection",
    description: "Xellent SQL Server connection string")
{ IsRequired = true };

var wattsOnUrlOption = new Option<string>(
    name: "--wattson-url",
    description: "WattsOn API base URL",
    getDefaultValue: () => "http://localhost:5100");

var supplierGlnOption = new Option<string>(
    name: "--supplier-gln",
    description: "Supplier GLN (EAN number)")
{ IsRequired = true };

var supplierNameOption = new Option<string>(
    name: "--supplier-name",
    description: "Supplier name",
    getDefaultValue: () => "Verdo");

var includeTimeSeriesOption = new Option<bool>(
    name: "--include-timeseries",
    description: "Include historical time series data",
    getDefaultValue: () => false);

var includeSettlementsOption = new Option<bool>(
    name: "--include-settlements",
    description: "Include historical settlements from FlexBillingHistory",
    getDefaultValue: () => false);

var timeSeriesStartOption = new Option<DateTime?>(
    name: "--timeseries-start",
    description: "Time series start date (default: 2 years ago)");

var dryRunOption = new Option<bool>(
    name: "--dry-run",
    description: "Extract and map but don't push to WattsOn",
    getDefaultValue: () => false);

var rootCommand = new RootCommand("WattsOn Migration Tool — migrate customers from Xellent to WattsOn")
{
    accountsOption,
    xellentConnectionOption,
    wattsOnUrlOption,
    supplierGlnOption,
    supplierNameOption,
    includeTimeSeriesOption,
    timeSeriesStartOption,
    includeSettlementsOption,
    dryRunOption
};

rootCommand.SetHandler(async (context) =>
{
    var accounts = context.ParseResult.GetValueForOption(accountsOption)!;
    var xellentConnection = context.ParseResult.GetValueForOption(xellentConnectionOption)!;
    var wattsOnUrl = context.ParseResult.GetValueForOption(wattsOnUrlOption)!;
    var supplierGln = context.ParseResult.GetValueForOption(supplierGlnOption)!;
    var supplierName = context.ParseResult.GetValueForOption(supplierNameOption)!;
    var includeTimeSeries = context.ParseResult.GetValueForOption(includeTimeSeriesOption);
    var timeSeriesStart = context.ParseResult.GetValueForOption(timeSeriesStartOption);
    var includeSettlements = context.ParseResult.GetValueForOption(includeSettlementsOption);
    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);

    // Setup DI
    var services = new ServiceCollection();
    services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

    services.AddDbContext<XellentDbContext>(options =>
        options.UseSqlServer(xellentConnection, o => o.UseCompatibilityLevel(110)));

    services.AddHttpClient<WattsOnMigrationClient>(client =>
        client.BaseAddress = new Uri(wattsOnUrl));

    services.AddTransient<XellentExtractionService>();

    var sp = services.BuildServiceProvider();
    var logger = sp.GetRequiredService<ILogger<Program>>();
    var extraction = sp.GetRequiredService<XellentExtractionService>();
    var wattsOn = sp.GetRequiredService<WattsOnMigrationClient>();

    var sw = Stopwatch.StartNew();
    var result = new MigrationResult();

    try
    {
        logger.LogInformation("Starting migration for {Count} accounts: {Accounts}",
            accounts.Length, string.Join(", ", accounts));

        // Step 1: Extract from Xellent
        logger.LogInformation("Extracting from Xellent...");
        var customers = await extraction.ExtractCustomersAsync(accounts);
        var products = await extraction.ExtractDistinctProductsAsync(accounts);
        logger.LogInformation("Extracted {CustomerCount} customers, {ProductCount} products, {MpCount} metering points",
            customers.Count, products.Count, customers.Sum(c => c.MeteringPoints.Count));

        if (dryRun)
        {
            logger.LogInformation("DRY RUN — skipping WattsOn API calls");
            foreach (var c in customers)
            {
                logger.LogInformation("  Customer: {Name} ({Id}) — {MpCount} metering points",
                    c.Name, c.Cpr ?? c.Cvr ?? "?", c.MeteringPoints.Count);
                foreach (var mp in c.MeteringPoints)
                {
                    logger.LogInformation("    MP: {Gsrn} ({Area}) supply {Start} → {End}, {ProductCount} product periods",
                        mp.Gsrn, mp.GridArea, mp.SupplyStart, mp.SupplyEnd?.ToString() ?? "open",
                        mp.ProductPeriods.Count);
                }
            }
            foreach (var p in products)
            {
                logger.LogInformation("  Product: {Name} — {RateCount} rate periods", p.Name, p.Rates.Count);
            }
            return;
        }

        // Step 2: Ensure supplier identity
        logger.LogInformation("Ensuring supplier identity {Gln}...", supplierGln);
        var supplierIdentityId = await wattsOn.EnsureSupplierIdentity(supplierGln, supplierName);
        logger.LogInformation("Supplier identity: {Id}", supplierIdentityId);

        // Step 3: Create products
        logger.LogInformation("Migrating {Count} products...", products.Count);
        var productResult = await wattsOn.MigrateSupplierProducts(new
        {
            supplierIdentityId,
            products = products.Select(p => new
            {
                p.Name, p.Description,
                pricingModel = p.PricingModel,
                isActive = true
            }).ToList()
        });
        result.ProductsCreated = productResult.GetProperty("created").GetInt32();
        result.ProductsSkipped = productResult.GetProperty("skipped").GetInt32();

        // Build product name → ID map from response
        var productIdMap = new Dictionary<string, Guid>();
        foreach (var kv in productResult.GetProperty("products").EnumerateObject())
            productIdMap[kv.Name] = kv.Value.GetGuid();

        // Step 4: Migrate customers (with metering points + supplies)
        logger.LogInformation("Migrating {Count} customers...", customers.Count);
        var customerPayload = new
        {
            supplierIdentityId,
            customers = customers.Select(c => new
            {
                name = c.Name,
                cpr = c.Cpr,
                cvr = c.Cvr,
                email = c.Email,
                phone = c.Phone,
                meteringPoints = c.MeteringPoints.Select(mp => new
                {
                    gsrn = mp.Gsrn,
                    type = "Forbrug",
                    art = "Fysisk",
                    settlementMethod = "Flex",
                    gridArea = mp.GridArea,
                    gridOperatorGln = mp.GridOperatorGln,
                    supplyStart = mp.SupplyStart,
                    supplyEnd = mp.SupplyEnd
                }).ToList()
            }).ToList()
        };
        var customerResult = await wattsOn.MigrateCustomers(customerPayload);
        result.CustomersCreated = customerResult.GetProperty("customersCreated").GetInt32();
        result.CustomersSkipped = customerResult.GetProperty("skipped").GetInt32();
        result.MeteringPointsCreated = customerResult.GetProperty("meteringPointsCreated").GetInt32();
        result.SuppliesCreated = customerResult.GetProperty("suppliesCreated").GetInt32();

        // Step 5: Supply product periods
        var allPeriods = customers
            .SelectMany(c => c.MeteringPoints)
            .SelectMany(mp => mp.ProductPeriods.Select(pp => new
            {
                gsrn = mp.Gsrn,
                productName = pp.ProductName,
                productStart = pp.Start,
                productEnd = pp.End
            }))
            .ToList();

        if (allPeriods.Count > 0)
        {
            logger.LogInformation("Migrating {Count} product periods...", allPeriods.Count);
            var periodResult = await wattsOn.MigrateSupplyProductPeriods(new
            {
                supplierIdentityId,
                periods = allPeriods
            });
            result.ProductPeriodsCreated = periodResult.GetProperty("created").GetInt32();
            result.ProductPeriodsSkipped = periodResult.GetProperty("skipped").GetInt32();
        }

        // Step 6: Supplier margins (flat rate per period, not hourly)
        foreach (var product in products.Where(p => p.Rates.Count > 0))
        {
            if (!productIdMap.TryGetValue(product.Name, out var productId))
            {
                result.Warnings.Add($"No product ID for '{product.Name}' — margins skipped");
                continue;
            }

            logger.LogInformation("Migrating {RateCount} margin rates for product {Name} ({Model})...",
                product.Rates.Count, product.Name, product.PricingModel);

            var marginResult = await wattsOn.MigrateSupplierMargins(new
            {
                supplierProductId = productId,
                rates = product.Rates.Select(r => new
                {
                    validFrom = r.StartDate,
                    priceDkkPerKwh = r.RateDkkPerKwh
                }).ToList()
            });
            result.MarginsCreated += marginResult.GetProperty("inserted").GetInt32();
        }

        // Step 7: DataHub prices (charges + rate history)
        logger.LogInformation("Extracting DataHub prices...");
        var allGsrnsForPrices = customers.SelectMany(c => c.MeteringPoints).Select(mp => mp.Gsrn).ToList();
        var extractedPrices = await extraction.ExtractPricesAsync(allGsrnsForPrices);

        if (extractedPrices.Count > 0)
        {
            logger.LogInformation("Migrating {Count} DataHub charges...", extractedPrices.Count);
            var priceResult = await wattsOn.MigratePrices(new
            {
                prices = extractedPrices.Select(p => new
                {
                    chargeId = p.ChargeId,
                    ownerGln = p.OwnerGln,
                    type = p.Type,
                    description = p.Description,
                    effectiveDate = p.EffectiveDate,
                    resolution = p.Resolution,
                    isTax = p.IsTax,
                    isPassThrough = p.IsPassThrough,
                    category = p.Category,
                    points = p.Points.Select(pt => new
                    {
                        timestamp = pt.Timestamp,
                        price = pt.Price
                    }).ToList()
                }).ToList()
            });
            result.PricesCreated = priceResult.GetProperty("created").GetInt32();
            result.PricesUpdated = priceResult.GetProperty("updated").GetInt32();
            result.PricePointsCreated = priceResult.GetProperty("pointsCreated").GetInt32();
        }

        // Step 8: Price links (charge → metering point)
        logger.LogInformation("Extracting price links...");
        var extractedLinks = await extraction.ExtractPriceLinksAsync(allGsrnsForPrices);

        if (extractedLinks.Count > 0)
        {
            logger.LogInformation("Migrating {Count} price links...", extractedLinks.Count);
            var linkResult = await wattsOn.MigratePriceLinks(new
            {
                links = extractedLinks.Select(l => new
                {
                    gsrn = l.Gsrn,
                    chargeId = l.ChargeId,
                    ownerGln = l.OwnerGln,
                    effectiveDate = l.EffectiveDate
                }).ToList()
            });
            result.PriceLinksCreated = linkResult.GetProperty("created").GetInt32();
            result.PriceLinksSkipped = linkResult.GetProperty("skipped").GetInt32();
        }

        // Step 9: Time series (if requested)
        if (includeTimeSeries)
        {
            var tsStart = timeSeriesStart ?? DateTime.UtcNow.AddYears(-2);
            logger.LogInformation("Extracting time series from {Start}...", tsStart);
            var allGsrns = customers.SelectMany(c => c.MeteringPoints).Select(mp => mp.Gsrn).ToList();
            var timeSeries = await extraction.ExtractTimeSeriesAsync(allGsrns, new DateTimeOffset(tsStart, TimeSpan.Zero));

            if (timeSeries.Count > 0)
            {
                logger.LogInformation("Migrating {Count} time series...", timeSeries.Count);
                var tsResult = await wattsOn.MigrateTimeSeries(new
                {
                    timeSeries = timeSeries.Select(ts => new
                    {
                        gsrn = ts.Gsrn,
                        periodStart = ts.PeriodStart,
                        periodEnd = ts.PeriodEnd,
                        resolution = ts.Resolution,
                        observations = ts.Observations.Select(o => new
                        {
                            timestamp = o.Timestamp,
                            kwh = o.Kwh,
                            quality = o.Quality
                        }).ToList()
                    }).ToList()
                });
                result.TimeSeriesCreated = tsResult.GetProperty("seriesCreated").GetInt32();
                result.ObservationsCreated = tsResult.GetProperty("observationsCreated").GetInt32();
                result.TimeSeriesSkipped = tsResult.GetProperty("skipped").GetInt32();
            }
        }

        // Step 10: Settlements (if requested — MUST run after prices for correct PriceId linking)
        if (includeSettlements)
        {
            logger.LogInformation("Extracting settlements from FlexBillingHistory...");
            var allGsrns = customers.SelectMany(c => c.MeteringPoints).Select(mp => mp.Gsrn).ToList();
            var settlements = await extraction.ExtractSettlementsAsync(allGsrns);

            if (settlements.Count > 0)
            {
                logger.LogInformation("Migrating {Count} settlements...", settlements.Count);
                var settlementResult = await wattsOn.MigrateSettlements(new
                {
                    settlements = settlements.Select(s => new
                    {
                        gsrn = s.Gsrn,
                        periodStart = s.PeriodStart,
                        periodEnd = s.PeriodEnd,
                        billingLogNum = s.BillingLogNum,
                        externalInvoiceReference = s.HistKeyNumber,
                        totalEnergyKwh = s.TotalEnergyKwh,
                        spotAmountDkk = s.SpotAmountDkk,
                        marginAmountDkk = s.MarginAmountDkk,
                        tariffLines = s.TariffLines.Select(t => new
                        {
                            chargeId = t.PartyChargeTypeId,
                            description = t.Description,
                            energyKwh = t.EnergyKwh,
                            avgUnitPrice = t.AvgUnitPrice
                        }).ToList()
                    }).ToList()
                });
                result.SettlementsCreated = settlementResult.GetProperty("created").GetInt32();
                result.SettlementsSkipped = settlementResult.GetProperty("skipped").GetInt32();
                var noMp = settlementResult.GetProperty("skippedNoMp").GetInt32();
                var noSupply = settlementResult.GetProperty("skippedNoSupply").GetInt32();
                var exists = settlementResult.GetProperty("skippedExists").GetInt32();
                if (noMp > 0 || noSupply > 0 || exists > 0)
                    logger.LogWarning("Settlement skip reasons: {NoMp} no MP, {NoSupply} no supply, {Exists} already exists",
                        noMp, noSupply, exists);
            }
        }

        sw.Stop();
        result.Duration = sw.Elapsed;
        logger.LogInformation("{Result}", result.ToString());
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Migration failed");
        result.Errors.Add(ex.Message);
    }
});

return await rootCommand.InvokeAsync(args);
