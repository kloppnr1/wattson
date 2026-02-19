using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Messaging;
using WattsOn.Infrastructure.Persistence;
using WattsOn.Worker.Routing;

namespace WattsOn.Worker;

/// <summary>
/// Polls for unprocessed inbox messages and routes them to the appropriate BRS handler.
/// The actual routing logic lives in <see cref="InboxMessageRouter"/> and individual handler classes.
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
                await RouteMessage(db, message, ct);
                message.MarkProcessed();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process message {MessageId} ({DocType}/{BizProcess})",
                    message.MessageId, message.DocumentType, message.BusinessProcess);
                message.MarkFailed(ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Routes a single inbox message. Kept as pass-through for backward compatibility
    /// with integration tests that call worker.RouteMessage() directly.
    /// </summary>
    internal async Task RouteMessage(WattsOnDbContext db, InboxMessage message, CancellationToken ct)
    {
        var router = new InboxMessageRouter(_logger);
        await router.RouteMessage(db, message, ct);
    }
}
