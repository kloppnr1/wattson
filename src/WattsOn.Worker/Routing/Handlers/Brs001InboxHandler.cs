using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs001InboxHandler
{
    private readonly ILogger _logger;
    public Brs001InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        switch (message.DocumentType)
        {
            case "RSM-001": // Confirmation from DataHub
            {
                var gsrn = PayloadParser.GetString(payload, "gsrn");
                var transactionId = PayloadParser.GetString(payload, "transactionId") ?? message.MessageId;

                if (string.IsNullOrEmpty(gsrn))
                {
                    _logger.LogWarning("BRS-001 RSM-001 missing GSRN — skipping message {MessageId}", message.MessageId);
                    break;
                }

                // Find the active BRS-001 process for this GSRN (Include Transitions so EF tracks new children correctly)
                var process = await db.Processes
                    .Include(p => p.Transitions)
                    .Where(p => p.ProcessType == ProcessType.Leverandørskift)
                    .Where(p => p.MeteringPointGsrn != null && p.MeteringPointGsrn.Value == gsrn)
                    .Where(p => p.Status != ProcessStatus.Completed && p.Status != ProcessStatus.Rejected)
                    .OrderByDescending(p => p.StartedAt)
                    .FirstOrDefaultAsync(ct);

                if (process != null)
                {
                    Brs001Handler.HandleConfirmation(process, transactionId);
                    _logger.LogInformation("BRS-001 confirmed for GSRN {Gsrn}, transaction {TransactionId}", gsrn, transactionId);

                    // Auto-execute if we have the customer data
                    var effectiveDate = process.EffectiveDate;
                    if (effectiveDate.HasValue)
                    {
                        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn, ct);
                        if (mp != null)
                        {
                            var supply = await db.Supplies
                                .Where(s => s.MeteringPointId == mp.Id)
                                .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > effectiveDate.Value)
                                .FirstOrDefaultAsync(ct);

                            // Find our actor
                            var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive, ct);

                            // Find or get customer from process context
                            var customerId = supply?.CustomerId;
                            if (customerId.HasValue)
                            {
                                var customer = await db.Customers.FindAsync(new object[] { customerId.Value }, ct);
                                if (customer != null && identity != null)
                                {
                                    var result = Brs001Handler.ExecuteSupplierChange(
                                        process, mp, customer, supply);

                                    if (result.NewSupply != null)
                                        db.Supplies.Add(result.NewSupply);
                                }
                            }
                        }
                    }
                }
                break;
            }

            case "RSM-004": // Stop-of-supply notification (we're losing a customer)
            {
                var gsrn = PayloadParser.GetString(payload, "gsrn");
                var effectiveDateStr = PayloadParser.GetString(payload, "effectiveDate");
                var newSupplierGln = PayloadParser.GetString(payload, "newSupplierGln");
                var transactionId = PayloadParser.GetString(payload, "transactionId") ?? message.MessageId;

                if (gsrn == null || effectiveDateStr == null) break;

                var effectiveDate = DateTimeOffset.Parse(effectiveDateStr);
                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn, ct);
                if (mp == null) break;

                var currentSupply = await db.Supplies
                    .Where(s => s.MeteringPointId == mp.Id)
                    .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > effectiveDate)
                    .FirstOrDefaultAsync(ct);

                if (currentSupply != null)
                {
                    var result = Brs001Handler.HandleAsRecipient(
                        Gsrn.Create(gsrn),
                        effectiveDate,
                        transactionId,
                        GlnNumber.Create(newSupplierGln ?? "5790000000005"),
                        currentSupply);

                    db.Processes.Add(result.Process);
                    _logger.LogInformation("BRS-001 recipient: losing supply for GSRN {Gsrn} effective {Date}",
                        gsrn, effectiveDate);
                }
                break;
            }
        }
    }
}
