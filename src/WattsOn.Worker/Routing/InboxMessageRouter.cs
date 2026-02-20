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

        var payload = CimPayloadExtractor.ExtractPayload(message.RawPayload, message.BusinessProcess, message.DocumentType);

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

            case "BRS-004":
                await new Brs004InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-007":
                await new Brs007InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-008":
                await new Brs008InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-013":
                await new Brs013InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-036":
                await new Brs036InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-044":
                await new Brs044InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-003":
                await new Brs003InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-011":
                await new Brs011InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-034":
                await new Brs034InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-038":
                await new Brs038InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-005":
                await new Brs005InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-024":
                await new Brs024InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-025":
                await new Brs025InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-039":
                await new Brs039InboxHandler(_logger).Handle(db, message, payload, ct);
                break;

            case "BRS-041":
                await new Brs041InboxHandler(_logger).Handle(db, message, payload, ct);
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
