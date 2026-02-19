using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs044InboxHandler
{
    private readonly ILogger _logger;
    public Brs044InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        var gsrnStr = PayloadParser.GetString(payload, "gsrn");
        if (string.IsNullOrEmpty(gsrnStr))
        {
            _logger.LogWarning("BRS-044: Missing GSRN — skipping message {MessageId}", message.MessageId);
            return;
        }

        var gsrn = Gsrn.Create(gsrnStr);
        var effectiveDateStr = PayloadParser.GetString(payload, "effectiveDate");
        var effectiveDate = effectiveDateStr != null
            ? DateTimeOffset.Parse(effectiveDateStr)
            : DateTimeOffset.UtcNow;

        var direction = PayloadParser.GetString(payload, "direction");

        if (string.Equals(direction, "incoming", StringComparison.OrdinalIgnoreCase))
        {
            await HandleIncoming(db, gsrn, gsrnStr, effectiveDate, payload, ct);
        }
        else
        {
            // Default to outgoing if not explicitly incoming
            await HandleOutgoing(db, gsrn, gsrnStr, effectiveDate, ct);
        }
    }

    private async Task HandleIncoming(
        WattsOnDbContext db, Gsrn gsrn, string gsrnStr,
        DateTimeOffset effectiveDate, JsonElement payload, CancellationToken ct)
    {
        // Find or skip if MP doesn't exist
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-044 incoming: Metering point not found for GSRN {Gsrn} — skipping", gsrnStr);
            return;
        }

        // Find customer by identifier from payload
        var customerIdentifier = PayloadParser.GetString(payload, "customerIdentifier");
        Guid customerId;

        if (!string.IsNullOrEmpty(customerIdentifier))
        {
            var customer = await db.Customers
                .Where(c => (c.Cpr != null && c.Cpr.Value == customerIdentifier) ||
                            (c.Cvr != null && c.Cvr.Value == customerIdentifier))
                .FirstOrDefaultAsync(ct);

            if (customer is null)
            {
                // Create a placeholder customer
                var customerName = PayloadParser.GetString(payload, "customerName") ?? "Unknown";
                var identity = await db.SupplierIdentities.FirstOrDefaultAsync(s => s.IsActive, ct);
                var identityId = identity?.Id ?? Guid.Empty;

                if (customerIdentifier.Length == 10)
                    customer = Domain.Entities.Customer.CreatePerson(customerName, CprNumber.Create(customerIdentifier), identityId);
                else if (customerIdentifier.Length == 8)
                    customer = Domain.Entities.Customer.CreateCompany(customerName, CvrNumber.Create(customerIdentifier), identityId);
                else
                {
                    _logger.LogWarning("BRS-044: Cannot determine customer type from identifier {Id}", customerIdentifier);
                    return;
                }

                db.Customers.Add(customer);
            }

            customerId = customer.Id;
        }
        else
        {
            _logger.LogWarning("BRS-044 incoming: No customer identifier for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var data = new Brs044Handler.IncomingTransferData(gsrn, effectiveDate, customerId, mp.Id);
        var result = Brs044Handler.HandleIncomingTransfer(data);

        db.Processes.Add(result.Process);
        if (result.NewSupply is not null) db.Supplies.Add(result.NewSupply);
        mp.SetActiveSupply(true);

        _logger.LogInformation("BRS-044 incoming: Created supply for GSRN {Gsrn} effective {Date}",
            gsrnStr, effectiveDate);
    }

    private async Task HandleOutgoing(
        WattsOnDbContext db, Gsrn gsrn, string gsrnStr,
        DateTimeOffset effectiveDate, CancellationToken ct)
    {
        var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn.Value, ct);
        if (mp is null)
        {
            _logger.LogWarning("BRS-044 outgoing: Metering point not found for GSRN {Gsrn}", gsrnStr);
            return;
        }

        var activeSupply = await db.Supplies
            .Where(s => s.MeteringPointId == mp.Id)
            .Where(s => s.SupplyPeriod.End == null || s.SupplyPeriod.End > effectiveDate)
            .FirstOrDefaultAsync(ct);

        var data = new Brs044Handler.OutgoingTransferData(gsrn, effectiveDate);
        var result = Brs044Handler.HandleOutgoingTransfer(data, mp, activeSupply);

        db.Processes.Add(result.Process);

        _logger.LogInformation("BRS-044 outgoing: Ended supply for GSRN {Gsrn} effective {Date}, had supply: {Had}",
            gsrnStr, effectiveDate, result.EndedSupply is not null);
    }
}
