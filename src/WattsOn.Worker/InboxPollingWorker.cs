using Microsoft.EntityFrameworkCore;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Polls for unprocessed inbox messages and processes them.
/// In production, this would call DataHub Peek/Dequeue API.
/// For now, it processes any InboxMessages that land in the DB.
/// </summary>
public class InboxPollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<InboxPollingWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(10);

    public InboxPollingWorker(IServiceScopeFactory scopeFactory, ILogger<InboxPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("InboxPollingWorker starting — polling every {Interval}s", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessages(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing inbox messages");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingMessages(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        var pending = await db.InboxMessages
            .Where(m => !m.IsProcessed && m.ProcessingAttempts < 5)
            .OrderBy(m => m.ReceivedAt)
            .Take(10)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        _logger.LogInformation("Processing {Count} inbox messages", pending.Count);

        foreach (var message in pending)
        {
            try
            {
                // TODO: Route to appropriate BRS handler based on DocumentType/BusinessProcess
                // For now, just mark as processed
                _logger.LogInformation("Processing message {MessageId} ({DocumentType}/{BusinessProcess})",
                    message.MessageId, message.DocumentType, message.BusinessProcess);

                message.MarkProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process message {MessageId}", message.MessageId);
                message.MarkFailed(ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
