using Microsoft.Extensions.Logging;
using WattsOn.Domain.Messaging;
using WattsOn.Infrastructure.Persistence;
using WattsOn.Worker.Routing.Handlers;

namespace WattsOn.Worker.Routing;

/// <summary>
/// Routes inbox messages to the appropriate BRS handler.
/// Internal for testability (InternalsVisibleTo).
/// </summary>
internal class InboxMessageRouter
{
    private readonly ILogger _logger;

    public InboxMessageRouter(ILogger logger)
    {
        _logger = logger;
    }

    public async Task RouteMessage(WattsOnDbContext db, InboxMessage message, CancellationToken ct)
    {
        _logger.LogInformation("Routing message {MessageId}: {DocumentType}/{BusinessProcess}",
            message.MessageId, message.DocumentType, message.BusinessProcess);

        var payload = PayloadParser.Parse(message.RawPayload);

        switch (message.BusinessProcess)
        {
            case "BRS-001":
                await new Brs001InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-002":
                await new Brs002InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-006":
                await new Brs006InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-009":
                await new Brs009InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-015":
                await new Brs015InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-021":
                await new Brs021InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-023":
                await new Brs023InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-027":
                await new Brs027InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-031":
            case "BRS-037": // Price link updates — same D17/D08/D18 message format as BRS-031
                await new Brs031InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            default:
                // Unknown business process — log and mark as processed
                _logger.LogInformation(
                    "No handler for business process '{BusinessProcess}' — marking as processed",
                    message.BusinessProcess);
                break;
        }
    }
}
