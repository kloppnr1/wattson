using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Services;
using WattsOn.Domain.ValueObjects;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker.Routing.Handlers;

internal class Brs009InboxHandler
{
    private readonly ILogger _logger;
    public Brs009InboxHandler(ILogger logger) => _logger = logger;

    public async Task Handle(WattsOnDbContext db, InboxMessage message, JsonElement payload, CancellationToken ct)
    {
        switch (message.DocumentType)
        {
            case "RSM-001": // Move-in confirmation
            {
                var gsrn = PayloadParser.GetString(payload, "gsrn");
                var transactionId = PayloadParser.GetString(payload, "transactionId") ?? message.MessageId;

                _logger.LogInformation("BRS-009 move-in confirmation for GSRN {Gsrn}", gsrn);
                // Find and update the process (Include Transitions so EF tracks new children correctly)
                var process = await db.Processes
                    .Include(p => p.Transitions)
                    .Where(p => p.ProcessType == ProcessType.Tilflytning)
                    .Where(p => p.MeteringPointGsrn != null && p.MeteringPointGsrn.Value == gsrn)
                    .Where(p => p.Status != ProcessStatus.Completed)
                    .OrderByDescending(p => p.StartedAt)
                    .FirstOrDefaultAsync(ct);

                if (process != null)
                {
                    process.MarkSubmitted(transactionId);
                    process.MarkConfirmed();
                    process.MarkCompleted();
                    _logger.LogInformation("BRS-009 process {ProcessId} completed for GSRN {Gsrn}", process.Id, gsrn);
                }
                break;
            }

            case "RSM-004": // Move-out notification
            {
                var gsrn = PayloadParser.GetString(payload, "gsrn");
                var effectiveDateStr = PayloadParser.GetString(payload, "effectiveDate");

                if (gsrn == null || effectiveDateStr == null) break;

                var effectiveDate = DateTimeOffset.Parse(effectiveDateStr);
                _logger.LogInformation("BRS-009 move-out for GSRN {Gsrn} effective {Date}", gsrn, effectiveDate);

                var mp = await db.MeteringPoints.FirstOrDefaultAsync(m => m.Gsrn.Value == gsrn, ct);
                if (mp == null) break;

                var supply = await db.Supplies
                    .Where(s => s.MeteringPointId == mp.Id)
                    .Where(s => s.SupplyPeriod.End == null)
                    .FirstOrDefaultAsync(ct);

                if (supply != null)
                {
                    var (process, _) = Brs009Handler.ExecuteMoveOut(
                        Gsrn.Create(gsrn), effectiveDate, supply);
                    db.Processes.Add(process);
                }
                break;
            }
        }
    }
}
