using Microsoft.EntityFrameworkCore;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Dispatches pending outbox messages to DataHub.
/// In production, this would POST to DataHub's API.
/// For now, it simulates sending by marking them as sent.
/// </summary>
public class OutboxDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatchWorker> _logger;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(15);

    public OutboxDispatchWorker(IServiceScopeFactory scopeFactory, ILogger<OutboxDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatchWorker starting — polling every {Interval}s", _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingMessages(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching outbox messages");
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }
    }

    private async Task DispatchPendingMessages(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        var now = DateTimeOffset.UtcNow;
        var pending = await db.OutboxMessages
            .Where(m => !m.IsSent && m.SendAttempts < 5
                && (m.ScheduledFor == null || m.ScheduledFor <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        _logger.LogInformation("Dispatching {Count} outbox messages", pending.Count);

        foreach (var message in pending)
        {
            try
            {
                // TODO: Send to DataHub API
                // For now, simulate successful send
                _logger.LogInformation("Dispatching message {DocumentType} to {Receiver}",
                    message.DocumentType, message.ReceiverGln);

                message.MarkSent(response: "{\"status\":\"accepted\"}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to dispatch message to {Receiver}", message.ReceiverGln);
                message.MarkFailed(ex.Message);
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
