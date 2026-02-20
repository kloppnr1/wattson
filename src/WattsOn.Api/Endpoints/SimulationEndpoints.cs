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

            // 8. Link existing prices to the metering point via BRS-037 D17 messages (like DataHub would)
            var existingLinks = await db.PriceLinks
                .Where(pt => pt.MeteringPointId == mp.Id)
                .CountAsync();

            var newPriceLinks = 0;
            if (existingLinks == 0)
            {
                var allPrices = await db.Prices.ToListAsync();
                foreach (var pris in allPrices)
                {
                    // Skip spot prices for wrong grid area
                    // Create D17 inbox message (same format BRS-037 sends from DataHub)
                    var d17TransactionId = Guid.NewGuid().ToString();
                    var d17Payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["businessReason"] = "D17",
                        ["gsrn"] = req.Gsrn,
                        ["chargeId"] = pris.ChargeId,
                        ["ownerGln"] = pris.OwnerGln.Value,
                        ["linkStart"] = req.EffectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    });
                    var d17Envelope = BuildIncomingCimEnvelope(
                        "NotifyChargeLinks", "D17", "D17",
                        pris.OwnerGln.Value, identity.Gln.Value,
                        d17TransactionId,
                        new Dictionary<string, object?>
                        {
                            ["marketEvaluationPoint.mRID"] = CimGsrn(req.Gsrn),
                            ["start_DateAndOrTime.dateTime"] = CimDateTime(req.EffectiveDate),
                        });
                    var d17Msg = InboxMessage.Create(
                        $"BRS-037-D17-{pris.ChargeId}", "D17",
                        pris.OwnerGln.Value, identity.Gln.Value,
                        d17Payload, "BRS-037");
                    d17Msg.MarkProcessed(null);
                    db.InboxMessages.Add(d17Msg);

                    // Also create the actual link (handler would do this)
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
                var periodEnd = periodStart.AddMonths(1);

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

            // Link prices via BRS-037 D17 messages (like DataHub would)
            var existingLinks = await db.PriceLinks
                .Where(pt => pt.MeteringPointId == mp.Id).CountAsync();
            var newPriceLinks = 0;
            if (existingLinks == 0)
            {
                foreach (var pris in await db.Prices.ToListAsync())
                {
                    var d17Payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["businessReason"] = "D17",
                        ["gsrn"] = req.Gsrn,
                        ["chargeId"] = pris.ChargeId,
                        ["ownerGln"] = pris.OwnerGln.Value,
                        ["linkStart"] = req.EffectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    });
                    var d17Envelope = BuildIncomingCimEnvelope(
                        "NotifyChargeLinks", "D17", "D17",
                        pris.OwnerGln.Value, identity.Gln.Value,
                        Guid.NewGuid().ToString(),
                        new Dictionary<string, object?>
                        {
                            ["marketEvaluationPoint.mRID"] = CimGsrn(req.Gsrn),
                            ["start_DateAndOrTime.dateTime"] = CimDateTime(req.EffectiveDate),
                        });
                    var d17Msg = InboxMessage.Create(
                        $"BRS-037-D17-{pris.ChargeId}", "D17",
                        pris.OwnerGln.Value, identity.Gln.Value,
                        d17Payload, "BRS-037");
                    d17Msg.MarkProcessed(null);
                    db.InboxMessages.Add(d17Msg);

                    db.PriceLinks.Add(PriceLink.Create(mp.Id, pris.Id, Period.From(req.EffectiveDate)));
                    newPriceLinks++;
                }
            }

            // Generate consumption data
            TimeSeries? time_series = null;
            if (req.GenerateConsumption)
            {
                var periodStart = req.EffectiveDate;
                var periodEnd = periodStart.AddMonths(1);

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
        /// Simulate BRS-031: DataHub sends price information for all mandatory price elements.
        /// Creates realistic Danish electricity market prices via inbox messages:
        /// - Nettarif (grid tariff, hourly differentiated)
        /// - Systemtarif (TSO system tariff)
        /// - Transmissionstarif (TSO transmission tariff)
        /// - Elafgift (electricity tax)
        /// - Balancetarif for forbrug (balance tariff)
        /// - Net abonnement (grid subscription)
        /// - Leverandørmargin (supplier margin, our own revenue)
        /// Each price gets a D18 (masterdata) + D08 (price points) inbox message.
        /// </summary>
        app.MapPost("/api/simulation/price-update", async (SimulatePriceUpdateRequest req, WattsOnDbContext db) =>
        {
            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(a => a.IsActive);
            if (identity is null) return Results.Problem("No supplier identity configured");

            var gridGln = req.GridCompanyGln ?? "5790000432752"; // Default grid company (Radius)
            var tsoGln = "5790000432752"; // Energinet (TSO)
            var effectiveDate = req.EffectiveDate ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)).ToOffset(TimeSpan.Zero); // Jan 1 2026 CET → UTC
            var gridArea = req.GridArea ?? "DK1";

            // Define all mandatory price elements from DataHub
            var priceDefinitions = new[]
            {
                new {
                    ChargeId = "NET-T-" + gridArea,
                    OwnerGln = gridGln,
                    Type = "Tarif",
                    Description = "Nettarif — " + gridArea,
                    Resolution = "PT1H",
                    IsTax = false,
                    IsPassThrough = true,
                    Category = PriceCategory.Nettarif,
                    // Hourly-differentiated grid tariff (øre/kWh) — Radius-style time-of-use
                    GetHourlyPrice = (Func<int, decimal>)(hour => hour switch
                    {
                        >= 0 and <= 5 => 0.1367m,    // Lavlast (low)
                        >= 6 and <= 16 => 0.4099m,   // Højlast (high)
                        >= 17 and <= 20 => 1.0113m,  // Spidslast (peak)
                        _ => 0.4099m,                 // Højlast
                    }),
                },
                new {
                    ChargeId = "SYS-T-01",
                    OwnerGln = tsoGln,
                    Type = "Tarif",
                    Description = "Systemtarif",
                    Resolution = "PT1H",
                    IsTax = false,
                    IsPassThrough = true,
                    Category = PriceCategory.Systemtarif,
                    GetHourlyPrice = (Func<int, decimal>)(_ => 0.054m), // Flat 5,4 øre/kWh
                },
                new {
                    ChargeId = "TRANS-T-01",
                    OwnerGln = tsoGln,
                    Type = "Tarif",
                    Description = "Transmissionstarif",
                    Resolution = "PT1H",
                    IsTax = false,
                    IsPassThrough = true,
                    Category = PriceCategory.Transmissionstarif,
                    GetHourlyPrice = (Func<int, decimal>)(_ => 0.049m), // Flat 4,9 øre/kWh
                },
                new {
                    ChargeId = "ELAFG-01",
                    OwnerGln = tsoGln,
                    Type = "Tarif",
                    Description = "Elafgift",
                    Resolution = "PT1H",
                    IsTax = true,
                    IsPassThrough = true,
                    Category = PriceCategory.Elafgift,
                    GetHourlyPrice = (Func<int, decimal>)(_ => 0.7610m), // 76,1 øre/kWh (2026 rate)
                },
                new {
                    ChargeId = "BAL-T-01",
                    OwnerGln = tsoGln,
                    Type = "Tarif",
                    Description = "Balancetarif for forbrug",
                    Resolution = "PT1H",
                    IsTax = false,
                    IsPassThrough = true,
                    Category = PriceCategory.Balancetarif,
                    GetHourlyPrice = (Func<int, decimal>)(_ => 0.00229m), // 0,229 øre/kWh
                },
                new {
                    ChargeId = "NET-ABO-" + gridArea,
                    OwnerGln = gridGln,
                    Type = "Abonnement",
                    Description = "Net abonnement — " + gridArea,
                    Resolution = (string?)null!,
                    IsTax = false,
                    IsPassThrough = true,
                    Category = PriceCategory.NetAbonnement,
                    GetHourlyPrice = (Func<int, decimal>)(_ => 21.56m), // ~258 kr/year ÷ 12
                },
            };

            var createdPrices = new List<object>();
            var messageCount = 0;
            var endDate = effectiveDate.AddMonths(1);

            foreach (var def in priceDefinitions)
            {
                var transactionId = Guid.NewGuid().ToString();

                // D18: Price masterdata
                var d18Payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                {
                    ["businessReason"] = "D18",
                    ["chargeId"] = def.ChargeId,
                    ["ownerGln"] = def.OwnerGln,
                    ["priceType"] = def.Type,
                    ["description"] = def.Description,
                    ["effectiveDate"] = effectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["resolution"] = def.Resolution ?? "P1M",
                    ["vatExempt"] = false,
                    ["isTax"] = def.IsTax,
                    ["isPassThrough"] = def.IsPassThrough,
                });

                var d18Envelope = BuildIncomingCimEnvelope(
                    "NotifyPriceList", "D18", "D18",
                    def.OwnerGln, identity.Gln.Value,
                    transactionId,
                    new Dictionary<string, object?>
                    {
                        ["originalTransactionIDReference_MktActivityRecord.mRID"] = transactionId,
                        ["start_DateAndOrTime.dateTime"] = CimDateTime(effectiveDate),
                    });

                var d18Msg = InboxMessage.Create(
                    $"BRS-031-D18-{def.ChargeId}", "D18",
                    def.OwnerGln, identity.Gln.Value,
                    d18Payload, "BRS-031");
                d18Msg.MarkProcessed(null);
                db.InboxMessages.Add(d18Msg);
                messageCount++;

                // D08: Price points
                if (def.Type == "Tarif" && def.Resolution == "PT1H")
                {
                    // Hourly-differentiated tariff — generate one month of hourly prices
                    var points = new List<Dictionary<string, object>>();
                    var hours = (int)(endDate - effectiveDate).TotalHours;
                    for (int i = 0; i < hours; i++)
                    {
                        var ts = effectiveDate.AddHours(i);
                        var hour = ts.Hour;
                        points.Add(new Dictionary<string, object>
                        {
                            ["timestamp"] = ts.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                            ["price"] = def.GetHourlyPrice(hour),
                        });
                    }

                    var d08Payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["businessReason"] = "D08",
                        ["chargeId"] = def.ChargeId,
                        ["ownerGln"] = def.OwnerGln,
                        ["startDate"] = effectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["endDate"] = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["points"] = points,
                    });

                    var d08Envelope = BuildIncomingCimEnvelope(
                        "NotifyPriceList", "D08", "D08",
                        def.OwnerGln, identity.Gln.Value,
                        Guid.NewGuid().ToString(),
                        new Dictionary<string, object?>
                        {
                            ["start_DateAndOrTime.dateTime"] = CimDateTime(effectiveDate),
                            ["end_DateAndOrTime.dateTime"] = CimDateTime(endDate),
                        });

                    var d08Msg = InboxMessage.Create(
                        $"BRS-031-D08-{def.ChargeId}", "D08",
                        def.OwnerGln, identity.Gln.Value,
                        d08Payload, "BRS-031");
                    d08Msg.MarkProcessed(null);
                    db.InboxMessages.Add(d08Msg);
                    messageCount++;
                }
                else if (def.Type == "Abonnement")
                {
                    // Subscription — single monthly price point
                    var d08Payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
                    {
                        ["businessReason"] = "D08",
                        ["chargeId"] = def.ChargeId,
                        ["ownerGln"] = def.OwnerGln,
                        ["startDate"] = effectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["endDate"] = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                        ["points"] = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["timestamp"] = effectiveDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                                ["price"] = def.GetHourlyPrice(0),
                            }
                        },
                    });

                    var d08Envelope = BuildIncomingCimEnvelope(
                        "NotifyPriceList", "D08", "D08",
                        def.OwnerGln, identity.Gln.Value,
                        Guid.NewGuid().ToString(),
                        new Dictionary<string, object?>
                        {
                            ["start_DateAndOrTime.dateTime"] = CimDateTime(effectiveDate),
                            ["end_DateAndOrTime.dateTime"] = CimDateTime(endDate),
                        });

                    var d08Msg = InboxMessage.Create(
                        $"BRS-031-D08-{def.ChargeId}", "D08",
                        def.OwnerGln, identity.Gln.Value,
                        d08Payload, "BRS-031");
                    d08Msg.MarkProcessed(null);
                    db.InboxMessages.Add(d08Msg);
                    messageCount++;
                }

                createdPrices.Add(new
                {
                    chargeId = def.ChargeId,
                    ownerGln = def.OwnerGln,
                    type = def.Type,
                    description = def.Description,
                    resolution = def.Resolution,
                    isTax = def.IsTax,
                });
            }

            // Process all messages through the BRS-031 handler
            var unprocessedMessages = await db.InboxMessages
                .Where(m => m.BusinessProcess == "BRS-031" && m.ProcessedAt == null)
                .OrderBy(m => m.ReceivedAt)
                .ToListAsync();

            // Re-fetch to process (handler needs them unprocessed)
            // Actually, we marked them processed above for the audit trail.
            // Instead, process them directly through the domain handler.
            foreach (var def in priceDefinitions)
            {
                var ownerGln = GlnNumber.Create(def.OwnerGln);
                var priceType = Enum.Parse<PriceType>(def.Type);
                var resolution = def.Resolution != null ? Enum.Parse<Resolution>(def.Resolution) : (Resolution?)null;

                // Create or update price via domain logic (same as Brs031Handler D18)
                var existingPrice = await db.Prices
                    .Include(p => p.PricePoints)
                    .Where(p => p.ChargeId == def.ChargeId)
                    .FirstOrDefaultAsync(p => p.OwnerGln.Value == ownerGln.Value);

                var price = existingPrice;

                if (price is null)
                {
                    price = Price.Create(
                        def.ChargeId, ownerGln, priceType, def.Description,
                        Period.From(effectiveDate), false, resolution,
                        def.IsTax, def.IsPassThrough, category: def.Category);
                    db.Prices.Add(price);
                }
                else
                {
                    price.UpdatePriceInfo(def.Description, def.IsTax, def.IsPassThrough);
                    price.UpdateCategory(def.Category);
                    price.UpdateValidity(Period.From(effectiveDate));
                }

                // Add price points (D08)
                if (def.Type == "Tarif" && def.Resolution == "PT1H")
                {
                    var hours = (int)(endDate - effectiveDate).TotalHours;
                    var points = new List<(DateTimeOffset, decimal)>();
                    for (int i = 0; i < hours; i++)
                    {
                        var ts = effectiveDate.AddHours(i);
                        points.Add((ts, def.GetHourlyPrice(ts.Hour)));
                    }
                    price.ReplacePricePoints(effectiveDate, endDate, points);
                }
                else if (def.Type == "Abonnement")
                {
                    price.ReplacePricePoints(effectiveDate, endDate,
                        new[] { (effectiveDate, def.GetHourlyPrice(0)) });
                }
            }

            // --- Spot prices (from Energi Data Service, not DataHub) ---
            // Uses same domain service as POST /api/spot-prices
            var totalHours = (int)(endDate - effectiveDate).TotalHours;
            var spotPoints = new List<(DateTimeOffset Timestamp, decimal PriceDkkPerKwh)>();
            for (int h = 0; h < totalHours; h++)
            {
                var ts = effectiveDate.AddHours(h);
                for (int q = 0; q < 4; q++)
                {
                    var qTs = ts.AddMinutes(q * 15);
                    // Simulate realistic spot price pattern: 0.30-1.20 DKK/kWh, peaking 17-20
                    var baseSpot = 0.45m + 0.15m * (decimal)Math.Sin(h * Math.PI / 12);
                    var spotPrice = baseSpot + (q * 0.01m);
                    spotPoints.Add((qTs, spotPrice));
                }
            }

            var existingSpots = await db.SpotPrices
                .Where(sp => sp.PriceArea == gridArea && sp.Timestamp >= effectiveDate && sp.Timestamp < endDate)
                .ToDictionaryAsync(sp => sp.Timestamp);
            var spotResult = SpotPriceService.Upsert(gridArea, spotPoints, existingSpots, e => db.SpotPrices.Add(e));

            // --- Supplier margin (supplier's own markup, not DataHub) ---
            // Uses same domain service as POST /api/supplier-margins
            var marginPoints = new List<(DateTimeOffset Timestamp, decimal PriceDkkPerKwh)>();
            for (int h = 0; h < totalHours; h++)
            {
                marginPoints.Add((effectiveDate.AddHours(h), 0.15m)); // 15 øre/kWh
            }

            var existingMargins = await db.SupplierMargins
                .Where(m => m.SupplierIdentityId == identity.Id && m.Timestamp >= effectiveDate && m.Timestamp < endDate)
                .ToDictionaryAsync(m => m.Timestamp);
            var marginResult = SupplierMarginService.Upsert(identity.Id, marginPoints, existingMargins, e => db.SupplierMargins.Add(e));

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                pricesCreated = createdPrices.Count,
                spotPricesCreated = spotResult.Inserted + spotResult.Updated,
                supplierMarginsCreated = marginResult.Inserted + marginResult.Updated,
                inboxMessagesCreated = messageCount,
                effectiveDate,
                endDate,
                gridCompanyGln = gridGln,
                gridArea,
                prices = createdPrices,
                message = $"BRS-031: {createdPrices.Count} regulerede DataHub-priser, {spotResult.Inserted + spotResult.Updated} spotpriser, " +
                          $"{marginResult.Inserted + marginResult.Updated} leverandørmarginer oprettet. " +
                          $"Periode: {effectiveDate:yyyy-MM-dd} — {endDate:yyyy-MM-dd}."
            });
        }).WithName("SimulatePriceUpdate");

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
