using Microsoft.EntityFrameworkCore;
using WattsOn.Api.Models;
using WattsOn.Domain.Entities;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Api.Endpoints;

public static class SimulationEndpoints
{
    /// <summary>
    /// Builds a realistic CIM JSON envelope as DataHub would send it (incoming message).
    /// Sender is DataHub (DDZ role), receiver is us (DDQ role).
    /// </summary>
    private static string BuildIncomingCimEnvelope(
        string documentName, string typeCode, string processType,
        string senderGln, string receiverGln,
        string transactionId, Dictionary<string, object?> transactionFields,
        string senderRole = "DDZ", string receiverRole = "DDQ")
    {
        var transaction = new Dictionary<string, object?>
        {
            ["mRID"] = transactionId
        };
        foreach (var kvp in transactionFields)
            if (kvp.Value is not null)
                transaction[kvp.Key] = kvp.Value;

        var document = new Dictionary<string, object?>
        {
            ["mRID"] = Guid.NewGuid().ToString(),
            ["type"] = new { value = typeCode },
            ["process.processType"] = new { value = processType },
            ["businessSector.type"] = new { value = "23" },
            ["sender_MarketParticipant.mRID"] = new { codingScheme = "A10", value = senderGln },
            ["sender_MarketParticipant.marketRole.type"] = new { value = senderRole },
            ["receiver_MarketParticipant.mRID"] = new { codingScheme = "A10", value = receiverGln },
            ["receiver_MarketParticipant.marketRole.type"] = new { value = receiverRole },
            ["createdDateTime"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["MktActivityRecord"] = new[] { transaction }
        };

        var envelope = new Dictionary<string, object?> { [documentName] = document };
        return System.Text.Json.JsonSerializer.Serialize(envelope);
    }

    private static object CimGsrn(string gsrn) => new { codingScheme = "A10", value = gsrn };
    private static object CimDateTime(DateTimeOffset dt) => dt.ToString("yyyy-MM-ddTHH:mm:ssZ");

    public static WebApplication MapSimulationEndpoints(this WebApplication app)
    {
        /// <summary>
        /// Simulate a full BRS-001 supplier change.
        /// Creates all necessary entities (customer, metering point if needed) and
        /// runs the entire process flow including DataHub message simulation.
        /// This is the "no seed data" approach — the system proves itself.
        /// </summary>
        app.MapPost("/api/simulation/supplier-change", async (SimulateSupplierChangeRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
            if (identity is null) return Results.Problem("No supplier identity configured");

            // 1. Find or create the metering point
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == req.Gsrn);
            if (mp is null)
            {
                var gridGln = GlnNumber.Create(req.GridCompanyGln ?? "5790000610976");
                var address = req.Address != null
                    ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                        req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
                    : null;
                mp = MeteringPoint.Create(gsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
                    SettlementMethod.Flex, Resolution.PT1H, req.GridArea ?? "DK1", gridGln, address);
                db.MeteringPoints.Add(mp);
            }

            // 2. Find or create the customer
            Customer? customer = null;
            if (req.CprNumber != null)
                customer = await db.Customers.FirstOrDefaultAsync(k => k.Cpr != null && k.Cpr.Value == req.CprNumber);
            else if (req.CvrNumber != null)
                customer = await db.Customers.FirstOrDefaultAsync(k => k.Cvr != null && k.Cvr.Value == req.CvrNumber);

            if (customer is null)
            {
                var address = req.Address != null
                    ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                        req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
                    : null;
                customer = req.CprNumber != null
                    ? Customer.CreatePerson(req.CustomerName, CprNumber.Create(req.CprNumber), identity.Id, address)
                    : Customer.CreateCompany(req.CustomerName, CvrNumber.Create(req.CvrNumber!), identity.Id, address);
                if (req.Email != null || req.Phone != null)
                    customer.UpdateContactInfo(req.Email, req.Phone);
                db.Customers.Add(customer);
            }

            // 3. Check for existing supply (if another supplier had this customer)
            var currentSupply = await db.Supplies
                .Where(l => l.MeteringPointId == mp.Id)
                .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > req.EffectiveDate)
                .FirstOrDefaultAsync();

            // 4. Create the BRS-001 process
            var process = Brs001Handler.InitiateSupplierChange(
                gsrn,
                req.EffectiveDate,
                req.CprNumber,
                req.CvrNumber,
                GlnNumber.Create(req.PreviousSupplierGln ?? "5790000000005"));

            // 5. Simulate DataHub confirmation
            var transactionId = $"DH-SIM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
            Brs001Handler.HandleConfirmation(process, transactionId);

            // 6. Execute the supplier change
            var result = Brs001Handler.ExecuteSupplierChange(
                process, mp, customer, currentSupply);

            mp.SetActiveSupply(true);

            // 7. Create simulated inbox messages with realistic CIM envelopes
            var requestMsg = InboxMessage.Create(
                $"MSG-{Guid.NewGuid().ToString()[..8]}",
                "RSM-001", "5790000432752", identity.Gln.Value,
                BuildIncomingCimEnvelope(
                    "RequestChangeOfSupplier_MarketDocument", "392", "E03",
                    "5790000432752", identity.Gln.Value, transactionId,
                    new Dictionary<string, object?>
                    {
                        ["marketEvaluationPoint.mRID"] = CimGsrn(req.Gsrn),
                        ["start_DateAndOrTime.dateTime"] = CimDateTime(req.EffectiveDate),
                        ["customer_MarketParticipant.mRID"] = req.CprNumber != null
                            ? new { codingScheme = "ARR", value = req.CprNumber }
                            : new { codingScheme = "VA", value = req.CvrNumber! },
                        ["customer_MarketParticipant.name"] = req.CustomerName,
                    }),
                "BRS-001");
            requestMsg.MarkProcessed(process.Id);

            var confirmMsg = InboxMessage.Create(
                $"MSG-{Guid.NewGuid().ToString()[..8]}",
                "RSM-001", "5790000432752", identity.Gln.Value,
                BuildIncomingCimEnvelope(
                    "ConfirmRequestChangeOfSupplier_MarketDocument", "A01", "E03",
                    "5790000432752", identity.Gln.Value, transactionId,
                    new Dictionary<string, object?>
                    {
                        ["originalTransactionIDReference_MktActivityRecord.mRID"] = transactionId,
                        ["marketEvaluationPoint.mRID"] = CimGsrn(req.Gsrn),
                        ["start_DateAndOrTime.dateTime"] = CimDateTime(req.EffectiveDate),
                    }),
                "BRS-001");
            confirmMsg.MarkProcessed(process.Id);

            // 8. Link standard prices to the metering point (if not already linked)
            var existingLinks = await db.PriceLinks
                .Where(pt => pt.MeteringPointId == mp.Id)
                .CountAsync();

            var newPriceLinks = 0;
            if (existingLinks == 0)
            {
                var allPrices = await db.Prices.ToListAsync();
                foreach (var pris in allPrices)
                {
                    var link = PriceLink.Create(mp.Id, pris.Id, Period.From(req.EffectiveDate));
                    db.PriceLinks.Add(link);
                    newPriceLinks++;
                }
            }

            // 9. Generate simulated time series with consumption data
            TimeSeries? time_series = null;
            if (req.GenerateConsumption)
            {
                var periodStart = req.EffectiveDate;
                // Generate one month of data from effective date
                var periodEnd = new DateTimeOffset(
                    periodStart.Year, periodStart.Month, 1, 0, 0, 0, periodStart.Offset)
                    .AddMonths(1);

                time_series = TimeSeries.Create(mp.Id, Period.Create(periodStart, periodEnd),
                    Resolution.PT1H, 1, $"SIM-{transactionId}");

                var hours = (int)(periodEnd - periodStart).TotalHours;
                var rng = new Random(req.Gsrn.GetHashCode()); // Deterministic per GSRN
                for (int i = 0; i < hours; i++)
                {
                    var ts = periodStart.AddHours(i);
                    var hour = ts.Hour;
                    // Realistic Danish household pattern
                    var baseKwh = hour switch
                    {
                        >= 23 or <= 5 => 0.3m + (decimal)(rng.NextDouble() * 0.4),   // Night: 0.3-0.7
                        >= 6 and <= 8 => 0.8m + (decimal)(rng.NextDouble() * 0.8),    // Morning: 0.8-1.6
                        >= 9 and <= 15 => 0.5m + (decimal)(rng.NextDouble() * 0.6),   // Day: 0.5-1.1
                        >= 16 and <= 19 => 1.2m + (decimal)(rng.NextDouble() * 1.2),  // Evening peak: 1.2-2.4
                        _ => 0.6m + (decimal)(rng.NextDouble() * 0.8),                // Late evening: 0.6-1.4
                    };
                    time_series.AddObservation(ts, EnergyQuantity.Create(baseKwh), QuantityQuality.Measured);
                }
                db.TimeSeriesCollection.Add(time_series);
            }

            // Save everything
            db.Processes.Add(process);
            if (result.NewSupply != null) db.Supplies.Add(result.NewSupply);
            db.InboxMessages.Add(requestMsg);
            db.InboxMessages.Add(confirmMsg);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processer/{process.Id}", new
            {
                processId = process.Id,
                transactionId,
                status = process.Status.ToString(),
                currentState = process.CurrentState,
                gsrn = req.Gsrn,
                customerName = customer.Name,
                customerId = customer.Id,
                meteringPointId = mp.Id,
                newSupplyId = result.NewSupply?.Id,
                endedSupplyId = result.EndedSupply?.Id,
                effectiveDate = req.EffectiveDate,
                priceLinksCreated = newPriceLinks,
                timeSeriesGenerated = time_series != null,
                totalEnergyKwh = time_series?.TotalEnergy.Value,
                message = $"Leverandørskift gennemført for {customer.Name} på {req.Gsrn}. " +
                          $"Effektiv dato: {req.EffectiveDate:yyyy-MM-dd}." +
                          (time_series != null ? $" Genereret {time_series.Observations.Count} timer forbrugsdata ({time_series.TotalEnergy.Value:F1} kWh)." : "") +
                          (newPriceLinks > 0 ? $" {newPriceLinks} prices tilknyttet." : "") +
                          " SettlementWorker beregner automatisk settlement inden for 30 secustomers."
            });
        }).WithName("SimulateSupplierChange");

        /// <summary>
        /// BRS-001 Recipient: Lose a customer — another supplier takes over.
        /// Ends the supply and triggers final settlement.
        /// </summary>
        app.MapPost("/api/simulation/supplier-change-outgoing", async (SimulateOutgoingSupplierChangeRequest req, WattsOnDbContext db) =>
        {
            // Find the supply
            var supply = await db.Supplies
                .Include(l => l.Customer)
                .Include(l => l.MeteringPoint)
                .Where(l => l.Id == req.SupplyId)
                .FirstOrDefaultAsync();

            if (supply is null)
                return Results.NotFound("Supply ikke fundet");

            if (!supply.IsActive)
                return Results.Problem("Supply er allerede afsluttet");

            var gsrn = supply.MeteringPoint.Gsrn;
            var newSupplierGln = GlnNumber.Create(req.NewSupplierGln ?? "5790000000005");

            var result = Brs001Handler.HandleAsRecipient(
                gsrn, req.EffectiveDate,
                $"DH-SIM-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}",
                newSupplierGln, supply);

            supply.MeteringPoint.SetActiveSupply(false);

            // Create audit trail inbox message with CIM envelope
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
            var msg = InboxMessage.Create(
                $"MSG-{Guid.NewGuid().ToString()[..8]}",
                "RSM-004", "5790000432752", identity?.Gln.Value ?? "unknown",
                BuildIncomingCimEnvelope(
                    "NotifyEndOfSupply_MarketDocument", "E44", "E03",
                    "5790000432752", identity?.Gln.Value ?? "unknown",
                    result.Process.TransactionId ?? Guid.NewGuid().ToString(),
                    new Dictionary<string, object?>
                    {
                        ["marketEvaluationPoint.mRID"] = CimGsrn(gsrn.Value),
                        ["start_DateAndOrTime.dateTime"] = CimDateTime(req.EffectiveDate),
                        ["in_MarketParticipant.mRID"] = new { codingScheme = "A10", value = req.NewSupplierGln ?? "5790000000005" },
                    }),
                "BRS-001");
            msg.MarkProcessed(result.Process.Id);

            db.Processes.Add(result.Process);
            db.InboxMessages.Add(msg);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                processId = result.Process.Id,
                transactionId = result.Process.TransactionId,
                status = result.Process.Status.ToString(),
                currentState = result.Process.CurrentState,
                gsrn = gsrn.Value,
                customerName = supply.Customer.Name,
                customerId = supply.Customer.Id,
                endedSupplyId = result.EndedSupply?.Id,
                effectiveDate = req.EffectiveDate,
                message = $"Leverandørskift udgående — {supply.Customer.Name} forlader os på {gsrn.Value}. " +
                          $"Supply afsluttet pr. {req.EffectiveDate:yyyy-MM-dd}. Slutsettlement beregnes automatisk."
            });
        }).WithName("SimulateOutgoingSupplierChange");

        /// <summary>
        /// BRS-009: Move-in — new customer at a metering point.
        /// </summary>
        app.MapPost("/api/simulation/move-in", async (SimulateMoveInRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
            if (identity is null) return Results.Problem("No supplier identity configured");

            // Find or create metering point
            var gsrn = Gsrn.Create(req.Gsrn);
            var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == req.Gsrn);
            if (mp is null)
            {
                var gridGln = GlnNumber.Create(req.GridCompanyGln ?? "5790000610976");
                var address = req.Address != null
                    ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                        req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
                    : null;
                mp = MeteringPoint.Create(gsrn, MeteringPointType.Forbrug, MeteringPointCategory.Fysisk,
                    SettlementMethod.Flex, Resolution.PT1H, req.GridArea ?? "DK1", gridGln, address);
                db.MeteringPoints.Add(mp);
            }

            // Find or create customer
            Customer? customer = null;
            if (req.CprNumber != null)
                customer = await db.Customers.FirstOrDefaultAsync(k => k.Cpr != null && k.Cpr.Value == req.CprNumber);
            else if (req.CvrNumber != null)
                customer = await db.Customers.FirstOrDefaultAsync(k => k.Cvr != null && k.Cvr.Value == req.CvrNumber);

            if (customer is null)
            {
                var address = req.Address != null
                    ? Address.Create(req.Address.StreetName, req.Address.BuildingNumber,
                        req.Address.PostCode, req.Address.CityName, req.Address.Floor, req.Address.Suite)
                    : null;
                customer = req.CprNumber != null
                    ? Customer.CreatePerson(req.CustomerName, CprNumber.Create(req.CprNumber), identity.Id, address)
                    : Customer.CreateCompany(req.CustomerName, CvrNumber.Create(req.CvrNumber!), identity.Id, address);
                if (req.Email != null || req.Phone != null)
                    customer.UpdateContactInfo(req.Email, req.Phone);
                db.Customers.Add(customer);
            }

            // Check for existing supply (previous tenant)
            var currentSupply = await db.Supplies
                .Where(l => l.MeteringPointId == mp.Id)
                .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > req.EffectiveDate)
                .FirstOrDefaultAsync();

            var result = Brs009Handler.ExecuteMoveIn(
                gsrn, req.EffectiveDate, req.CprNumber, req.CvrNumber,
                mp, customer, currentSupply);

            mp.SetActiveSupply(true);

            // Link prices
            var existingLinks = await db.PriceLinks
                .Where(pt => pt.MeteringPointId == mp.Id).CountAsync();
            var newPriceLinks = 0;
            if (existingLinks == 0)
            {
                foreach (var pris in await db.Prices.ToListAsync())
                {
                    db.PriceLinks.Add(PriceLink.Create(mp.Id, pris.Id, Period.From(req.EffectiveDate)));
                    newPriceLinks++;
                }
            }

            // Generate consumption data
            TimeSeries? time_series = null;
            if (req.GenerateConsumption)
            {
                var periodStart = req.EffectiveDate;
                var periodEnd = new DateTimeOffset(
                    periodStart.Year, periodStart.Month, 1, 0, 0, 0, periodStart.Offset).AddMonths(1);

                time_series = TimeSeries.Create(mp.Id, Period.Create(periodStart, periodEnd),
                    Resolution.PT1H, 1, $"SIM-{result.Process.TransactionId}");

                var hours = (int)(periodEnd - periodStart).TotalHours;
                var rng = new Random(req.Gsrn.GetHashCode());
                for (int i = 0; i < hours; i++)
                {
                    var ts = periodStart.AddHours(i);
                    var hour = ts.Hour;
                    var baseKwh = hour switch
                    {
                        >= 23 or <= 5 => 0.3m + (decimal)(rng.NextDouble() * 0.4),
                        >= 6 and <= 8 => 0.8m + (decimal)(rng.NextDouble() * 0.8),
                        >= 9 and <= 15 => 0.5m + (decimal)(rng.NextDouble() * 0.6),
                        >= 16 and <= 19 => 1.2m + (decimal)(rng.NextDouble() * 1.2),
                        _ => 0.6m + (decimal)(rng.NextDouble() * 0.8),
                    };
                    time_series.AddObservation(ts, EnergyQuantity.Create(baseKwh), QuantityQuality.Measured);
                }
                db.TimeSeriesCollection.Add(time_series);
            }

            // Audit trail with CIM envelope
            var inboxMsg = InboxMessage.Create(
                $"MSG-{Guid.NewGuid().ToString()[..8]}",
                "RSM-001", "5790000432752", identity.Gln.Value,
                BuildIncomingCimEnvelope(
                    "ConfirmRequestChangeOfSupplier_MarketDocument", "A01", "E65",
                    "5790000432752", identity.Gln.Value,
                    result.Process.TransactionId ?? Guid.NewGuid().ToString(),
                    new Dictionary<string, object?>
                    {
                        ["marketEvaluationPoint.mRID"] = CimGsrn(req.Gsrn),
                        ["start_DateAndOrTime.dateTime"] = CimDateTime(req.EffectiveDate),
                        ["customer_MarketParticipant.mRID"] = req.CprNumber != null
                            ? new { codingScheme = "ARR", value = req.CprNumber }
                            : new { codingScheme = "VA", value = req.CvrNumber! },
                        ["customer_MarketParticipant.name"] = req.CustomerName,
                    }),
                "BRS-009");
            inboxMsg.MarkProcessed(result.Process.Id);

            db.Processes.Add(result.Process);
            db.Supplies.Add(result.NewSupply);
            db.InboxMessages.Add(inboxMsg);
            await db.SaveChangesAsync();

            return Results.Created($"/api/processer/{result.Process.Id}", new
            {
                processId = result.Process.Id,
                transactionId = result.Process.TransactionId,
                status = result.Process.Status.ToString(),
                currentState = result.Process.CurrentState,
                gsrn = req.Gsrn,
                customerName = customer.Name,
                customerId = customer.Id,
                newSupplyId = result.NewSupply.Id,
                endedSupplyId = result.EndedSupply?.Id,
                previousCustomerName = result.EndedSupply != null ? "Tidligere lejer" : null,
                effectiveDate = req.EffectiveDate,
                priceLinksCreated = newPriceLinks,
                timeSeriesGenerated = time_series != null,
                totalEnergyKwh = time_series?.TotalEnergy.Value,
                message = $"Tilflytning gennemført for {customer.Name} på {req.Gsrn}. " +
                          $"Effektiv dato: {req.EffectiveDate:yyyy-MM-dd}." +
                          (result.EndedSupply != null ? " Tidligere supply afsluttet." : "") +
                          (time_series != null ? $" Genereret {time_series.Observations.Count} timer forbrugsdata ({time_series.TotalEnergy.Value:F1} kWh)." : "") +
                          (newPriceLinks > 0 ? $" {newPriceLinks} prices tilknyttet." : "") +
                          " SettlementWorker beregner automatisk settlement inden for 30 secustomers."
            });
        }).WithName("SimulateMoveIn");

        /// <summary>
        /// Move-out: Customer leaves a metering point. Ends supply, final settlement.
        /// </summary>
        app.MapPost("/api/simulation/move-out", async (SimulateMoveOutRequest req, WattsOnDbContext db) =>
        {
            var supply = await db.Supplies
                .Include(l => l.Customer)
                .Include(l => l.MeteringPoint)
                .Where(l => l.Id == req.SupplyId)
                .FirstOrDefaultAsync();

            if (supply is null)
                return Results.NotFound("Supply ikke fundet");

            if (!supply.IsActive)
                return Results.Problem("Supply er allerede afsluttet");

            var gsrn = supply.MeteringPoint.Gsrn;
            var result = Brs009Handler.ExecuteMoveOut(gsrn, req.EffectiveDate, supply);

            supply.MeteringPoint.SetActiveSupply(false);

            // Audit trail with CIM envelope
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
            var msg = InboxMessage.Create(
                $"MSG-{Guid.NewGuid().ToString()[..8]}",
                "RSM-001", "5790000432752", identity?.Gln.Value ?? "unknown",
                BuildIncomingCimEnvelope(
                    "ConfirmRequestChangeOfSupplier_MarketDocument", "A01", "E01",
                    "5790000432752", identity?.Gln.Value ?? "unknown",
                    result.Process.TransactionId ?? Guid.NewGuid().ToString(),
                    new Dictionary<string, object?>
                    {
                        ["marketEvaluationPoint.mRID"] = CimGsrn(gsrn.Value),
                        ["start_DateAndOrTime.dateTime"] = CimDateTime(req.EffectiveDate),
                    }),
                "BRS-010");
            msg.MarkProcessed(result.Process.Id);

            db.Processes.Add(result.Process);
            db.InboxMessages.Add(msg);
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                processId = result.Process.Id,
                transactionId = result.Process.TransactionId,
                status = result.Process.Status.ToString(),
                currentState = result.Process.CurrentState,
                gsrn = gsrn.Value,
                customerName = supply.Customer.Name,
                customerId = supply.Customer.Id,
                endedSupplyId = result.EndedSupply.Id,
                effectiveDate = req.EffectiveDate,
                message = $"Fraflytning gennemført — {supply.Customer.Name} fraflyttet {gsrn.Value}. " +
                          $"Supply afsluttet pr. {req.EffectiveDate:yyyy-MM-dd}. Slutsettlement beregnes automatisk."
            });
        }).WithName("SimulateMoveOut");

        /// <summary>
        /// Simulate corrected metered data — DataHub sends updated time series for an already-invoiced period.
        /// This triggers WattsOn's correction detection: the SettlementWorker detects the updated data,
        /// calculates the delta against the invoiced settlement, and creates an adjustment.
        ///
        /// Realistic CIM envelope: BRS-021/RSM-012 (NotifyValidatedMeasureData_MarketDocument)
        /// with process type E23 (metered data collection) and type code E66 (validated measure data).
        ///
        /// The variation is ±5-15% per hour, which is realistic for meter recalibrations,
        /// load profile corrections, and estimated-to-measured transitions.
        /// </summary>
        app.MapPost("/api/simulation/corrected-metered-data", async (SimulateCorrectedMeteredDataRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
            if (identity is null) return Results.Problem("No supplier identity configured");

            // 1. Find the invoiced settlement
            var settlement = await db.Settlements
                .Include(a => a.MeteringPoint)
                .Include(a => a.Lines)
                .FirstOrDefaultAsync(a => a.Id == req.SettlementId);

            if (settlement is null)
                return Results.NotFound("Settlement ikke fundet");

            if (settlement.Status != SettlementStatus.Invoiced)
                return Results.Problem($"Settlement har status '{settlement.Status}' — kun fakturerede settlements kan korrigeres");

            // 2. Find the original time series
            var originalTimeSeries = await db.TimeSeriesCollection
                .Include(ts => ts.Observations)
                .FirstOrDefaultAsync(ts =>
                    ts.Id == settlement.TimeSeriesId &&
                    ts.Version == settlement.TimeSeriesVersion);

            if (originalTimeSeries is null)
                return Results.Problem("Original tidsserie ikke fundet");

            // 3. Find the active supply for this metering point
            var supply = await db.Supplies
                .Where(l => l.MeteringPointId == settlement.MeteringPointId)
                .Where(l => l.SupplyPeriod.Start <= settlement.SettlementPeriod.Start)
                .Where(l => l.SupplyPeriod.End == null || l.SupplyPeriod.End > settlement.SettlementPeriod.Start)
                .FirstOrDefaultAsync();

            if (supply is null)
                return Results.Problem("Ingen aktiv leverance fundet for målepunktet");

            // 4. Mark old time series as superseded
            originalTimeSeries.Supersede();

            // 5. Generate new time series with ±5-15% random variation per hour
            var newVersion = originalTimeSeries.Version + 1;
            var transactionId = $"DH-COR-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";

            var correctedTimeSeries = TimeSeries.Create(
                settlement.MeteringPointId,
                originalTimeSeries.Period,
                originalTimeSeries.Resolution,
                newVersion,
                transactionId);

            var rng = new Random();
            var totalOriginalKwh = 0m;
            var totalCorrectedKwh = 0m;

            foreach (var obs in originalTimeSeries.Observations.OrderBy(o => o.Timestamp))
            {
                // Apply ±5-15% random variation
                var variationPercent = (decimal)(rng.NextDouble() * 0.10 + 0.05); // 5% to 15%
                var direction = rng.NextDouble() > 0.5 ? 1m : -1m;
                var variation = obs.Quantity.Value * variationPercent * direction;
                var newKwh = Math.Max(0.01m, obs.Quantity.Value + variation); // Ensure positive

                correctedTimeSeries.AddObservation(
                    obs.Timestamp,
                    EnergyQuantity.Create(newKwh),
                    QuantityQuality.Revised); // A05 = Revised — correct quality code for corrections

                totalOriginalKwh += obs.Quantity.Value;
                totalCorrectedKwh += newKwh;
            }

            db.TimeSeriesCollection.Add(correctedTimeSeries);

            // 6. Create realistic CIM inbox message for BRS-021/RSM-012
            // Build the time series observations as CIM "Point" elements
            var cimPoints = correctedTimeSeries.Observations
                .OrderBy(o => o.Timestamp)
                .Select((o, idx) => new Dictionary<string, object?>
                {
                    ["position"] = idx + 1,
                    ["quantity"] = o.Quantity.Value,
                    ["quality"] = new { value = "A05" } // Revised
                })
                .ToArray();

            var gsrn = settlement.MeteringPoint.Gsrn.Value;
            var inboxMsg = InboxMessage.Create(
                $"MSG-{Guid.NewGuid().ToString()[..8]}",
                "RSM-012", "5790000432752", identity.Gln.Value,
                BuildIncomingCimEnvelope(
                    "NotifyValidatedMeasureData_MarketDocument", "E66", "E23",
                    "5790000432752", identity.Gln.Value, transactionId,
                    new Dictionary<string, object?>
                    {
                        ["marketEvaluationPoint.mRID"] = CimGsrn(gsrn),
                        ["marketEvaluationPoint.type"] = new { value = "E17" }, // Consumption
                        ["product"] = "8716867000030", // Active energy
                        ["quantity_Measure_Unit.name"] = new { value = "KWH" },
                        ["registration_DateAndOrTime.dateTime"] = CimDateTime(DateTimeOffset.UtcNow),
                        ["Period"] = new
                        {
                            resolution = new { value = "PT1H" },
                            timeInterval = new
                            {
                                start = CimDateTime(originalTimeSeries.Period.Start),
                                end = CimDateTime(originalTimeSeries.Period.End!.Value)
                            },
                            Point = cimPoints
                        }
                    }),
                "BRS-021");
            inboxMsg.MarkProcessed(); // Auto-process — the SettlementWorker handles the actual settlement

            db.InboxMessages.Add(inboxMsg);
            await db.SaveChangesAsync();

            var deltaKwh = totalCorrectedKwh - totalOriginalKwh;
            var deltaPercent = totalOriginalKwh > 0 ? (deltaKwh / totalOriginalKwh) * 100m : 0m;

            return Results.Ok(new
            {
                originalSettlementId = settlement.Id,
                originalTimeSeriesVersion = originalTimeSeries.Version,
                correctedTimeSeriesId = correctedTimeSeries.Id,
                correctedTimeSeriesVersion = newVersion,
                transactionId,
                gsrn,
                periodStart = originalTimeSeries.Period.Start,
                periodEnd = originalTimeSeries.Period.End,
                originalTotalKwh = Math.Round(totalOriginalKwh, 3),
                correctedTotalKwh = Math.Round(totalCorrectedKwh, 3),
                deltaKwh = Math.Round(deltaKwh, 3),
                deltaPercent = Math.Round(deltaPercent, 1),
                observationCount = correctedTimeSeries.Observations.Count,
                message = $"Korrigeret måledata genereret for {gsrn}. " +
                          $"Version {originalTimeSeries.Version} → {newVersion}. " +
                          $"Delta: {deltaKwh:+0.0;-0.0} kWh ({deltaPercent:+0.0;-0.0}%). " +
                          $"SettlementWorker registrerer automatisk korrektionen inden for 30 sekunder."
            });
        }).WithName("SimulateCorrectedMeteredData");

        /// <summary>
        /// Get invoiced settlements available for correction simulation.
        /// Returns only settlements that have been invoiced and haven't already been adjusted.
        /// </summary>
        app.MapGet("/api/simulation/invoiced-settlements", async (WattsOnDbContext db) =>
        {
            var invoiced = await db.Settlements
                .Include(a => a.MeteringPoint)
                .Include(a => a.Supply)
                    .ThenInclude(l => l.Customer)
                .Where(a => a.Status == SettlementStatus.Invoiced)
                .AsNoTracking()
                .OrderByDescending(a => a.CalculatedAt)
                .Select(a => new
                {
                    a.Id,
                    gsrn = a.MeteringPoint.Gsrn.Value,
                    customerName = a.Supply.Customer.Name,
                    periodStart = a.SettlementPeriod.Start,
                    periodEnd = a.SettlementPeriod.End,
                    totalEnergyKwh = a.TotalEnergy.Value,
                    totalAmount = a.TotalAmount.Amount,
                    currency = a.TotalAmount.Currency,
                    externalInvoiceReference = a.ExternalInvoiceReference,
                    invoicedAt = a.InvoicedAt,
                    documentNumber = a.DocumentNumber,
                    calculatedAt = a.CalculatedAt
                })
                .ToListAsync();

            return Results.Ok(invoiced);
        }).WithName("GetInvoicedSettlementsForSimulation");

        return app;
    }
}
