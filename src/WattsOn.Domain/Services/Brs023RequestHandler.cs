using WattsOn.Domain.Enums;
using WattsOn.Domain.Messaging;
using WattsOn.Domain.Processes;
using WattsOn.Domain.ValueObjects;

namespace WattsOn.Domain.Services;

/// <summary>
/// Handles outbound BRS-023 — Request aggregated measure data from DataHub.
/// Creates an RSM-016 CIM request payload and queues it in outbox.
///
/// Request: RSM-016/E74 → DataHub (DGL role)
/// Response: RSM-014 (aggregated time series) → inbox → routed to Brs023Handler
///
/// ProcessType codes:
/// - D04 = Balancefiksering
/// - D05 = Engrosfiksering
/// - D32 = Korrektionsafregning
///
/// Grid area uses NDK coding scheme (NOT A10/GLN).
/// </summary>
public static class Brs023RequestHandler
{
    /// <summary>NDK = Danish grid area coding scheme.</summary>
    private const string CodingSchemeGridArea = "NDK";

    public record AggregatedDataRequestResult(
        BrsProcess Process,
        OutboxMessage OutboxMessage);

    /// <summary>
    /// Request aggregated measure data from DataHub for a grid area and period.
    /// </summary>
    /// <param name="supplierGln">Our GLN (sender)</param>
    /// <param name="gridArea">Grid area code (e.g. "543")</param>
    /// <param name="startDate">Period start</param>
    /// <param name="endDate">Period end</param>
    /// <param name="meteringPointType">E17=consumption, E18=production</param>
    /// <param name="processType">D04=balance, D05=wholesale, D32=correction</param>
    public static AggregatedDataRequestResult RequestAggregatedData(
        GlnNumber supplierGln,
        string gridArea,
        DateTimeOffset startDate,
        DateTimeOffset endDate,
        string meteringPointType = "E17",
        string processType = "D04")
    {
        if (string.IsNullOrWhiteSpace(gridArea))
            throw new ArgumentException("Grid area is required.", nameof(gridArea));
        if (endDate <= startDate)
            throw new ArgumentException("End date must be after start date.", nameof(endDate));

        var process = BrsProcess.Create(
            ProcessType.AggregetDataAnmodning,
            ProcessRole.Initiator,
            "Created",
            effectiveDate: startDate);

        var transactionId = $"BRS023-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid().ToString()[..8]}";
        process.TransitionTo("Submitted", "Anmodning om aggregerede data sendt til DataHub");
        process.MarkSubmitted(transactionId);

        var seriesFields = new Dictionary<string, object?>
        {
            ["meteringGridArea_Domain.mRID"] = new { codingScheme = CodingSchemeGridArea, value = gridArea },
            ["marketEvaluationPoint.type"] = new { value = meteringPointType },
            ["start_DateAndOrTime.dateTime"] = startDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["end_DateAndOrTime.dateTime"] = endDate.ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };

        var payload = CimDocumentBuilder
            .Create(RsmDocumentType.Rsm016, processType, supplierGln.Value)
            .AddSeries(seriesFields)
            .Build();

        var outbox = OutboxMessage.Create(
            documentType: "RSM-016",
            senderGln: supplierGln.Value,
            receiverGln: CimDocumentBuilder.DataHubGln,
            payload: payload,
            processId: process.Id,
            businessProcess: "BRS-023");

        return new AggregatedDataRequestResult(process, outbox);
    }

    /// <summary>
    /// DataHub rejected our aggregated data request.
    /// </summary>
    public static void HandleRejection(BrsProcess process, string reason)
    {
        process.TransitionTo("Rejected", reason);
        process.MarkRejected(reason);
    }

    /// <summary>
    /// Aggregated data received — mark process as completed.
    /// The actual data is processed by Brs023Handler.
    /// </summary>
    public static void HandleDataReceived(BrsProcess process)
    {
        process.TransitionTo("DataReceived", "Aggregerede data modtaget fra DataHub");
        process.MarkCompleted();
    }
}
