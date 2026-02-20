using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Messaging;
using WattsOn.Infrastructure.DataHub;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Polls DataHub B2B API queues via Peek, stores messages as InboxMessages,
/// and dequeues after confirmed storage. Completes the inbound pipeline:
/// DataHub → Peek → InboxMessage → CimPayloadExtractor → Handlers
///
/// Polling strategy (per DataHub requirements):
/// - Poll each queue in sequence
/// - If message found: process it, then peek again (drain the queue)
/// - If queue empty: move to next queue
/// - After all queues checked: wait before next cycle (configurable, default 60s)
/// - In simulation mode: does nothing (no DataHub to poll)
/// </summary>
public class DataHubInboxFetcher : BackgroundService
{
    /// <summary>Three DataHub peek queues (from edi-document-types.md).</summary>
    private static readonly (string Name, string Endpoint)[] PeekQueues =
    [
        ("processes",    "/processes"),     // Market process messages (BRS)
        ("measuredata",  "/measuredata"),   // Metered data, acknowledgements
        ("aggregations", "/aggregations"),  // Aggregated data, wholesale
    ];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataHubClient _dataHubClient;
    private readonly DataHubSettings _settings;
    private readonly ILogger<DataHubInboxFetcher> _logger;

    public DataHubInboxFetcher(
        IServiceScopeFactory scopeFactory,
        DataHubClient dataHubClient,
        Microsoft.Extensions.Options.IOptions<DataHubSettings> settings,
        ILogger<DataHubInboxFetcher> logger)
    {
        _scopeFactory = scopeFactory;
        _dataHubClient = dataHubClient;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_dataHubClient.IsSimulationMode)
        {
            _logger.LogInformation("DataHubInboxFetcher: Simulation mode — not polling DataHub");
            return; // Exit immediately, don't waste cycles
        }

        var pollInterval = TimeSpan.FromSeconds(_settings.InboxPollIntervalSeconds);
        _logger.LogInformation(
            "DataHubInboxFetcher starting — polling {QueueCount} queues every {Interval}s",
            PeekQueues.Length, _settings.InboxPollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var totalFetched = 0;

                foreach (var queue in PeekQueues)
                {
                    totalFetched += await DrainQueueAsync(queue, stoppingToken);
                }

                if (totalFetched > 0)
                    _logger.LogInformation("DataHubInboxFetcher: fetched {Count} messages this cycle", totalFetched);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in inbox fetch cycle");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Drain a single queue by peeking repeatedly until empty or error.
    /// </summary>
    private async Task<int> DrainQueueAsync((string Name, string Endpoint) queue, CancellationToken ct)
    {
        var count = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var peekResult = await _dataHubClient.PeekAsync(queue.Endpoint, ct);
                if (peekResult is null)
                    break; // Queue empty

                await ProcessPeekedMessageAsync(peekResult, queue.Name, ct);
                count++;
            }
            catch (OperationCanceledException)
            {
                throw; // Let cancellation propagate
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message from {Queue}", queue.Name);
                break; // Don't hammer DataHub on errors
            }
        }

        return count;
    }

    /// <summary>
    /// Store a peeked message as InboxMessage and dequeue from DataHub.
    /// </summary>
    private async Task ProcessPeekedMessageAsync(DataHubPeekResult peekResult, string queueName, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        // Dedup: check if we already have this message
        var alreadyExists = await db.InboxMessages.AnyAsync(
            m => m.MessageId == peekResult.MessageId, ct);

        if (alreadyExists)
        {
            _logger.LogDebug("Message {MessageId} already stored — dequeuing duplicate", peekResult.MessageId);
            await _dataHubClient.DequeueAsync(peekResult.MessageId, ct);
            return;
        }

        // Classify the CIM envelope
        var classification = CimMessageClassifier.Classify(peekResult.Payload);

        // Store as InboxMessage
        var inboxMessage = InboxMessage.Create(
            messageId: peekResult.MessageId,
            documentType: classification.DocumentType ?? "UNKNOWN",
            senderGln: classification.SenderGln ?? "UNKNOWN",
            receiverGln: classification.ReceiverGln ?? "UNKNOWN",
            rawPayload: peekResult.Payload,
            businessProcess: classification.BusinessProcess);

        db.InboxMessages.Add(inboxMessage);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Fetched {BRS}/{RSM} from {Queue} (msg {MessageId}, from {Sender})",
            classification.BusinessProcess ?? "?",
            classification.DocumentType ?? "?",
            queueName,
            peekResult.MessageId,
            classification.SenderGln ?? "?");

        // Dequeue from DataHub (message is safely stored)
        var dequeued = await _dataHubClient.DequeueAsync(peekResult.MessageId, ct);
        if (!dequeued)
            _logger.LogWarning("Failed to dequeue message {MessageId} — will be fetched again next cycle", peekResult.MessageId);
    }
}
