using WattsOn.Domain.Common;

namespace WattsOn.Domain.Messaging;

/// <summary>
/// OutboxMessage — message to be sent to DataHub.
/// Stored before sending for reliability and retry capability.
/// </summary>
public class OutboxMessage : Entity
{
    /// <summary>Document type to send (e.g., RSM-001, RSM-004)</summary>
    public string DocumentType { get; private set; } = null!;

    /// <summary>Business process type (e.g., BRS-001)</summary>
    public string? BusinessProcess { get; private set; }

    /// <summary>Our GLN (sender)</summary>
    public string SenderGln { get; private set; } = null!;

    /// <summary>Receiver GLN</summary>
    public string ReceiverGln { get; private set; } = null!;

    /// <summary>JSON payload to send</summary>
    public string Payload { get; private set; } = null!;

    /// <summary>Associated BRS process ID</summary>
    public Guid? ProcessId { get; private set; }

    /// <summary>Whether this message has been sent</summary>
    public bool IsSent { get; private set; }

    /// <summary>When the message was sent</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>DataHub response/acknowledgment</summary>
    public string? Response { get; private set; }

    /// <summary>Sending error if any</summary>
    public string? SendError { get; private set; }

    /// <summary>Number of send attempts</summary>
    public int SendAttempts { get; private set; }

    /// <summary>Scheduled send time (null = send immediately)</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    private OutboxMessage() { } // EF Core

    public static OutboxMessage Create(
        string documentType,
        string senderGln,
        string receiverGln,
        string payload,
        Guid? processId = null,
        string? businessProcess = null,
        DateTimeOffset? scheduledFor = null)
    {
        return new OutboxMessage
        {
            DocumentType = documentType,
            BusinessProcess = businessProcess,
            SenderGln = senderGln,
            ReceiverGln = receiverGln,
            Payload = payload,
            ProcessId = processId,
            IsSent = false,
            SendAttempts = 0,
            ScheduledFor = scheduledFor
        };
    }

    public void MarkSent(string? response = null)
    {
        IsSent = true;
        SentAt = DateTimeOffset.UtcNow;
        Response = response;
        MarkUpdated();
    }

    public void MarkFailed(string error)
    {
        SendAttempts++;
        SendError = error;
        MarkUpdated();
    }

    /// <summary>
    /// Reset a dead-lettered message for retry (clears error, resets attempts).
    /// </summary>
    public void ResetForRetry()
    {
        SendAttempts = 0;
        SendError = null;
        MarkUpdated();
    }
}
