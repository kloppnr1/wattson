using WattsOn.Domain.Common;

namespace WattsOn.Domain.Messaging;

/// <summary>
/// InboxMessage — raw message received from DataHub.
/// Stored before processing for audit trail and replay capability.
/// </summary>
public class InboxMessage : Entity
{
    /// <summary>DataHub message ID</summary>
    public string MessageId { get; private set; } = null!;

    /// <summary>Document type (e.g., RSM-001, RSM-012)</summary>
    public string DocumentType { get; private set; } = null!;

    /// <summary>Business process type (e.g., BRS-001)</summary>
    public string? BusinessProcess { get; private set; }

    /// <summary>Sender GLN</summary>
    public string SenderGln { get; private set; } = null!;

    /// <summary>Receiver GLN</summary>
    public string ReceiverGln { get; private set; } = null!;

    /// <summary>Raw JSON payload from DataHub</summary>
    public string RawPayload { get; private set; } = null!;

    /// <summary>When the message was received</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>Whether this message has been processed</summary>
    public bool IsProcessed { get; private set; }

    /// <summary>When the message was processed</summary>
    public DateTimeOffset? ProcessedAt { get; private set; }

    /// <summary>Processing error if any</summary>
    public string? ProcessingError { get; private set; }

    /// <summary>Number of processing attempts</summary>
    public int ProcessingAttempts { get; private set; }

    /// <summary>Associated BRS process ID (set after processing)</summary>
    public Guid? ProcessId { get; private set; }

    private InboxMessage() { } // EF Core

    public static InboxMessage Create(
        string messageId,
        string documentType,
        string senderGln,
        string receiverGln,
        string rawPayload,
        string? businessProcess = null)
    {
        return new InboxMessage
        {
            MessageId = messageId,
            DocumentType = documentType,
            BusinessProcess = businessProcess,
            SenderGln = senderGln,
            ReceiverGln = receiverGln,
            RawPayload = rawPayload,
            ReceivedAt = DateTimeOffset.UtcNow,
            IsProcessed = false,
            ProcessingAttempts = 0
        };
    }

    public void MarkProcessed(Guid? processId = null)
    {
        IsProcessed = true;
        ProcessedAt = DateTimeOffset.UtcNow;
        ProcessId = processId;
        MarkUpdated();
    }

    public void MarkFailed(string error)
    {
        ProcessingAttempts++;
        ProcessingError = error;
        MarkUpdated();
    }
}
