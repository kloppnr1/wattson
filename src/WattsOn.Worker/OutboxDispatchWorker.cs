using Microsoft.EntityFrameworkCore;
using WattsOn.Domain.Messaging;
using WattsOn.Infrastructure.DataHub;
using WattsOn.Infrastructure.Persistence;

namespace WattsOn.Worker;

/// <summary>
/// Dispatches pending outbox messages to DataHub via CIM JSON API.
/// Supports simulation mode (no credentials) and real mode (OAuth2 + POST).
/// Uses exponential backoff for transient failures and dead-letters rejected messages.
/// </summary>
public class OutboxDispatchWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DataHubClient _dataHubClient;
    private readonly DataHubSettings _settings;
    private readonly ILogger<OutboxDispatchWorker> _logger;

    public OutboxDispatchWorker(
        IServiceScopeFactory scopeFactory,
        DataHubClient dataHubClient,
        Microsoft.Extensions.Options.IOptions<DataHubSettings> settings,
        ILogger<OutboxDispatchWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _dataHubClient = dataHubClient;
        _settings = settings.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mode = _dataHubClient.IsSimulationMode ? "SIMULATION" : "LIVE";
        _logger.LogInformation(
            "OutboxDispatchWorker starting in {Mode} mode — polling every {Interval}s, max retries {MaxRetries}",
            mode, _settings.PollIntervalSeconds, _settings.MaxRetries);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchPendingMessages(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in outbox dispatch cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollIntervalSeconds), stoppingToken);
        }
    }

    private async Task DispatchPendingMessages(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WattsOnDbContext>();

        var now = DateTimeOffset.UtcNow;
        var pending = await db.OutboxMessages
            .Where(m => !m.IsSent
                && m.SendAttempts < _settings.MaxRetries
                && (m.ScheduledFor == null || m.ScheduledFor <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(_settings.BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        _logger.LogInformation("Dispatching {Count} outbox message(s)", pending.Count);

        foreach (var message in pending)
        {
            // Exponential backoff: skip if not enough time has passed since last failure
            if (message.SendAttempts > 0 && !IsReadyForRetry(message))
            {
                _logger.LogDebug(
                    "Skipping message {Id} — backoff not elapsed (attempt {Attempts})",
                    message.Id, message.SendAttempts);
                continue;
            }

            await DispatchSingleMessage(message, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task DispatchSingleMessage(OutboxMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "Dispatching {DocumentType}/{BusinessProcess} to {Receiver} (attempt {Attempt}/{Max})",
            message.DocumentType, message.BusinessProcess ?? "—",
            message.ReceiverGln, message.SendAttempts + 1, _settings.MaxRetries);

        var result = await _dataHubClient.SendAsync(message.DocumentType, message.Payload, ct);

        switch (result.Status)
        {
            case DataHubSendStatus.Accepted:
                message.MarkSent(result.ResponseBody);
                _logger.LogInformation(
                    "✓ Message {Id} ({DocumentType}) accepted by DataHub",
                    message.Id, message.DocumentType);
                break;

            case DataHubSendStatus.Rejected:
                // Permanent failure — don't retry, mark as dead-lettered
                message.MarkFailed($"REJECTED: {result.Error}");
                _logger.LogWarning(
                    "✗ Message {Id} ({DocumentType}) rejected by DataHub: {Error}",
                    message.Id, message.DocumentType, result.Error);
                break;

            case DataHubSendStatus.TransientFailure:
                message.MarkFailed(result.Error ?? "Transient failure");
                var nextRetryIn = GetBackoffDelay(message.SendAttempts);
                if (message.SendAttempts >= _settings.MaxRetries)
                {
                    _logger.LogError(
                        "✗ Message {Id} ({DocumentType}) dead-lettered after {Attempts} attempts. Last error: {Error}",
                        message.Id, message.DocumentType, message.SendAttempts, result.Error);
                }
                else
                {
                    _logger.LogWarning(
                        "⟳ Message {Id} ({DocumentType}) transient failure (attempt {Attempts}), next retry in ~{Delay}s: {Error}",
                        message.Id, message.DocumentType, message.SendAttempts, nextRetryIn.TotalSeconds, result.Error);
                }
                break;
        }
    }

    /// <summary>
    /// Check if enough time has elapsed since last failure for exponential backoff.
    /// </summary>
    private bool IsReadyForRetry(OutboxMessage message)
    {
        var backoff = GetBackoffDelay(message.SendAttempts);
        var lastAttempt = message.UpdatedAt;
        return DateTimeOffset.UtcNow >= lastAttempt + backoff;
    }

    /// <summary>
    /// Exponential backoff: base * 2^(attempts-1), capped at 30 minutes.
    /// Attempt 1: 30s, 2: 60s, 3: 120s, 4: 240s, 5: 480s (8min)
    /// </summary>
    private TimeSpan GetBackoffDelay(int attempts)
    {
        if (attempts <= 0) return TimeSpan.Zero;

        var seconds = _settings.RetryBaseDelaySeconds * Math.Pow(2, attempts - 1);
        var capped = Math.Min(seconds, 1800); // Cap at 30 minutes
        return TimeSpan.FromSeconds(capped);
    }
}
